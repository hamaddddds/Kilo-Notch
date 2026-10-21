package com.kilo.notch

import android.accessibilityservice.AccessibilityService
import android.annotation.SuppressLint
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.res.Configuration
import android.graphics.PixelFormat
import android.media.AudioDeviceCallback
import android.media.AudioDeviceInfo
import android.media.AudioManager
import android.os.BatteryManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.MotionEvent
import android.view.WindowInsets
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.widget.FrameLayout
import androidx.compose.ui.platform.ComposeView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.setViewTreeLifecycleOwner
import androidx.savedstate.SavedStateRegistry
import androidx.savedstate.SavedStateRegistryController
import androidx.savedstate.SavedStateRegistryOwner
import androidx.savedstate.setViewTreeSavedStateRegistryOwner
import kotlinx.coroutines.flow.MutableStateFlow

/** Where the camera is (px, display coordinates), so the island can sit right on it. */
data class Geo(
    val screenW: Int = 0, val statusBar: Int = 0,
    val camX: Int = 0,      // camera center, relative to the screen's center
    val camW: Int = 0, val camY: Int = 0, val camH: Int = 0,
    val landscape: Boolean = false, val barsHidden: Boolean = false,
)

/**
 * Hosts the island. An accessibility overlay is the one window type that sits above the status bar and
 * still takes taps, which is what an island on the camera needs. The window is only as big as the
 * island, so every touch outside it goes to the app underneath.
 */
class IslandService : AccessibilityService(), LifecycleOwner, SavedStateRegistryOwner {
    private val registry = LifecycleRegistry(this)
    private val saved = SavedStateRegistryController.create(this)
    override val lifecycle: Lifecycle get() = registry
    override val savedStateRegistry: SavedStateRegistry get() = saved.savedStateRegistry

    private val main = Handler(Looper.getMainLooper())
    private val params = WindowManager.LayoutParams()
    private var wm: WindowManager? = null
    private var host: Host? = null
    private var shrink: Runnable? = null
    private var seenBars = false
    private val geo = MutableStateFlow(Geo())

    override fun onCreate() {
        super.onCreate()
        Prefs.init(this)
        saved.performAttach()
        saved.performRestore(null)
        registry.handleLifecycleEvent(Lifecycle.Event.ON_CREATE)
    }

    override fun onServiceConnected() {
        val w = getSystemService(WINDOW_SERVICE) as WindowManager
        wm = w
        params.apply {
            type = WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY
            format = PixelFormat.TRANSLUCENT
            flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH or WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or
                WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS or WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED
            gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
            width = 1
            height = 1
            if (Build.VERSION.SDK_INT >= 30) layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS
            else if (Build.VERSION.SDK_INT >= 28) layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
        }
        measure()
        val h = Host(this).apply {
            setViewTreeLifecycleOwner(this@IslandService)
            setViewTreeSavedStateRegistryOwner(this@IslandService)
            onOutside = { Hub.expanded.value = false }
            addView(ComposeView(context).apply { setContent { IslandUi(geo, ::resize, ::place) } })
            setOnApplyWindowInsetsListener { v, insets -> bars(insets); v.onApplyWindowInsets(insets) }
        }
        host = h
        w.addView(h, params)
        registry.handleLifecycleEvent(Lifecycle.Event.ON_START)
        registry.handleLifecycleEvent(Lifecycle.Event.ON_RESUME)
        Hub.alive.value = true
        listen()
    }

    // ---------- window ----------

    /** The UI asks for a window size: grow at once, shrink only after the island has finished shrinking. */
    private fun resize(w: Int, h: Int) {
        val win = host ?: return
        shrink?.let(main::removeCallbacks)
        apply(maxOf(params.width, w), maxOf(params.height, h), win)
        if (w < params.width || h < params.height) {
            val later = Runnable { apply(w, h, win) }
            shrink = later
            main.postDelayed(later, 520)
        }
    }

    private fun place(xPx: Int) {
        if (params.x == xPx) return
        params.x = xPx
        host?.let { wm?.updateViewLayout(it, params) }
    }

    private fun apply(w: Int, h: Int, win: Host) {
        if (params.width == w && params.height == h) return
        params.width = w.coerceAtLeast(1)
        params.height = h.coerceAtLeast(1)
        if (win.isAttachedToWindow) wm?.updateViewLayout(win, params)
    }

    // ---------- the camera and the bars ----------

    @SuppressLint("DiscouragedApi", "InternalInsetResource")
    @Suppress("DEPRECATION")
    private fun measure() {
        val dm = resources.displayMetrics
        val bar = resources.getIdentifier("status_bar_height", "dimen", "android").let { if (it > 0) resources.getDimensionPixelSize(it) else (24 * dm.density).toInt() }
        val cut = if (Build.VERSION.SDK_INT >= 29) wm?.defaultDisplay?.cutout else null
        val cam = cut?.boundingRects?.filter { it.top <= bar && it.width() < dm.widthPixels / 2 }?.minByOrNull { it.top }
        geo.value = Geo(
            screenW = dm.widthPixels, statusBar = bar,
            camX = cam?.let { it.centerX() - dm.widthPixels / 2 } ?: 0,
            camW = cam?.width() ?: 0,
            camY = cam?.centerY() ?: bar / 2,
            camH = cam?.height() ?: 0,
            landscape = resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE,
            barsHidden = geo.value.barsHidden,
        )
    }

    // Full-screen video or a game hides the status bar: the island steps aside. Only trusted once the
    // bars have been seen visible, so a phone that never reports them can't hide the island for good.
    private fun bars(insets: WindowInsets) {
        if (Build.VERSION.SDK_INT < 30) return
        val visible = insets.isVisible(WindowInsets.Type.statusBars())
        if (visible) seenBars = true
        val hidden = seenBars && !visible
        if (hidden != geo.value.barsHidden) geo.value = geo.value.copy(barsHidden = hidden)
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        measure()
    }

    // ---------- battery and headphones ----------

    private var lastPlugged: Boolean? = null
    private var devicesReady = false

    private val power = object : BroadcastReceiver() {
        override fun onReceive(c: Context, i: Intent) {
            val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
            val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, 100)
            val pct = if (level >= 0 && scale > 0) level * 100 / scale else Hub.battery.value.first
            val plugged = i.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0
            Hub.battery.value = pct to plugged
            val was = lastPlugged
            lastPlugged = plugged
            if (was == false && plugged) {
                Hub.react(Mood.Happy)
                Hub.peek(Peek(Peek.Kind.Charging, "Charging", "$pct%", progress = pct / 100f, ms = 3500))
            }
        }
    }

    private val devices = object : AudioDeviceCallback() {
        override fun onAudioDevicesAdded(added: Array<out AudioDeviceInfo>) {
            if (!devicesReady) return // the first call lists what was already connected
            val d = added.firstOrNull { it.isSink && it.type in headphoneTypes } ?: return
            val name = d.productName?.toString()?.takeIf { it.isNotBlank() && it != Build.MODEL } ?: "Headphones"
            Hub.peek(Peek(Peek.Kind.Headphones, name, "Connected", ms = 3500))
        }
    }

    private val headphoneTypes = buildSet {
        addAll(listOf(AudioDeviceInfo.TYPE_BLUETOOTH_A2DP, AudioDeviceInfo.TYPE_WIRED_HEADPHONES, AudioDeviceInfo.TYPE_WIRED_HEADSET, AudioDeviceInfo.TYPE_USB_HEADSET))
        if (Build.VERSION.SDK_INT >= 31) add(AudioDeviceInfo.TYPE_BLE_HEADSET)
    }

    private fun listen() {
        registerReceiver(power, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        getSystemService(AudioManager::class.java).registerAudioDeviceCallback(devices, main)
        main.postDelayed({ devicesReady = true }, 1500)
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {}
    override fun onInterrupt() {}

    override fun onDestroy() {
        Hub.alive.value = false
        Hub.showing.value = false
        try { unregisterReceiver(power) } catch (e: IllegalArgumentException) { }
        getSystemService(AudioManager::class.java).unregisterAudioDeviceCallback(devices)
        host?.let { wm?.removeView(it) }
        host = null
        registry.handleLifecycleEvent(Lifecycle.Event.ON_DESTROY)
        super.onDestroy()
    }
}

/** The window's root: a tap outside the island arrives as ACTION_OUTSIDE and folds it back up. */
class Host(c: Context) : FrameLayout(c) {
    var onOutside: () -> Unit = {}

    override fun dispatchTouchEvent(e: MotionEvent): Boolean {
        if (e.actionMasked == MotionEvent.ACTION_OUTSIDE) { onOutside(); return true }
        return super.dispatchTouchEvent(e)
    }
}
