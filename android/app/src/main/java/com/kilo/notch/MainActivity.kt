package com.kilo.notch

import android.Manifest
import android.app.AlarmManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import kotlin.math.roundToInt

/** Kilo Notch's settings live in the app; long-press the island to get here. */
class MainActivity : ComponentActivity() {
    private val resumed = mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { SettingsScreen(resumed.intValue) }
    }

    // coming back from Android's settings: re-check what was granted
    override fun onResume() {
        super.onResume()
        resumed.intValue++
    }
}

/** What Kilo needs from Android, each with its own switch somewhere in Settings. */
object Setup {
    fun island(c: Context): Boolean {
        val me = ComponentName(c, IslandService::class.java)
        return Settings.Secure.getString(c.contentResolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES).orEmpty()
            .split(':').any { ComponentName.unflattenFromString(it) == me }
    }

    fun listener(c: Context) = c.packageName in NotificationManagerCompat.getEnabledListenerPackages(c)
    fun notify(c: Context) = Build.VERSION.SDK_INT < 33 ||
        ContextCompat.checkSelfPermission(c, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
    fun alarms(c: Context) = Build.VERSION.SDK_INT < 31 || c.getSystemService(AlarmManager::class.java).canScheduleExactAlarms()
    fun battery(c: Context) = c.getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(c.packageName)

    fun openIsland(c: Context) = go(c, Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
    fun openListener(c: Context) = go(c,
        if (Build.VERSION.SDK_INT >= 30) Intent(Settings.ACTION_NOTIFICATION_LISTENER_DETAIL_SETTINGS)
            .putExtra(Settings.EXTRA_NOTIFICATION_LISTENER_COMPONENT_NAME, ComponentName(c, NotifService::class.java).flattenToString())
        else Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS),
        Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
    fun openAlarms(c: Context) { if (Build.VERSION.SDK_INT >= 31) go(c, Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, Uri.parse("package:${c.packageName}"))) }
    @Suppress("BatteryLife")
    fun openBattery(c: Context) = go(c, Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:${c.packageName}")),
        Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
    fun openAppInfo(c: Context) = go(c, Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${c.packageName}")))

    private fun go(c: Context, vararg tries: Intent) {
        for (i in tries) try { c.startActivity(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); return } catch (e: Exception) { }
    }
}

@Composable
fun SettingsScreen(tick: Int) {
    val c = LocalContext.current
    Prefs.changed.collectAsState().value
    val (aI, bI) = Kolors.accent()
    val a = aI.color()
    val b = bI.color()
    LazyColumn(Modifier.fillMaxSize().background(Color.Black).systemBarsPadding(), contentPadding = PaddingValues(16.dp, 8.dp, 16.dp, 40.dp)) {
        item { Hero(a, b, tick) }
        item { SetupCard(a, b, tick) }
        item { IslandCard(a, b) }
        item { ColorsCard(a, b) }
        item { FocusCard(a, b) }
        item { WaterCard(a, b) }
        item { AppsCard(c, a) }
        item {
            Text("Kilo Notch ${version(c)} · long-press the island to come back here",
                color = Dim, fontSize = 12.sp, modifier = Modifier.fillMaxWidth().padding(top = 18.dp), textAlign = androidx.compose.ui.text.style.TextAlign.Center)
        }
    }
}

private fun version(c: Context): String = try { c.packageManager.getPackageInfo(c.packageName, 0).versionName.orEmpty() } catch (e: Exception) { "" }

@Composable
private fun Hero(a: Color, b: Color, tick: Int) {
    val alive by Hub.alive.collectAsState()
    val on = alive || Setup.island(LocalContext.current) // `tick` changes after a trip to Settings, so this re-checks
    val reaction by Hub.reaction.collectAsState()
    val mood = if (reaction.second > System.currentTimeMillis()) reaction.first else Mood.Normal
    Column(Modifier.fillMaxWidth().padding(top = 8.dp, bottom = 14.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        // a phone's top edge with the island on it
        Box(
            Modifier.fillMaxWidth().height(130.dp).clip(RoundedCornerShape(28.dp))
                .background(Brush.verticalGradient(listOf(Color(0xFF141A33), Color(0xFF1B1430))))
                .border(1.dp, Color(0xFF1D2030), RoundedCornerShape(28.dp)),
        ) {
            Box(
                Modifier.align(Alignment.TopCenter).padding(top = 14.dp).size(128.dp, 34.dp).clip(RoundedCornerShape(17.dp))
                    .background(Color.Black)
                    .background(Brush.verticalGradient(listOf(Color.Transparent, a.copy(alpha = 0.35f))))
                    .clickable { Hub.react(Mood.Happy, 1500) },
                contentAlignment = Alignment.Center,
            ) { Kilo(mood, 22.dp) }
            Text(
                if (on) "Your island is on. Tap Kilo." else "Finish the setup below to turn the island on",
                color = Dim, fontSize = 12.sp, modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = 16.dp),
            )
        }
        Spacer(Modifier.height(14.dp))
        Text("Kilo Notch", color = Color.White, fontSize = 26.sp, fontWeight = FontWeight.Bold)
        Text("A living island around your camera", color = Dim, fontSize = 14.sp)
    }
}

@Composable
private fun SetupCard(a: Color, b: Color, tick: Int) {
    val c = LocalContext.current
    val ask = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    val island = Setup.island(c)
    val listener = Setup.listener(c)
    Card("Setup") {
        Step("Island", "Draws the island above the status bar. Turn on \"Kilo Notch island\".", island, a) { Setup.openIsland(c) }
        Step("Notifications & music", "Shows notifications and what's playing in the island.", listener, a) { Setup.openListener(c) }
        if (Build.VERSION.SDK_INT >= 33) Step("Reminders", "Water and timer alerts, even when the app is closed.", Setup.notify(c), a) {
            ask.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        if (Build.VERSION.SDK_INT in 31..32) Step("On-time alarms", "So reminders ring at the minute, not later.", Setup.alarms(c), a) { Setup.openAlarms(c) }
        Step("Keep running", "Stops Android from putting Kilo to sleep. Reminders stay reliable.", Setup.battery(c), a) { Setup.openBattery(c) }
        if (!island || !listener) Hint(
            "Switch greyed out?",
            "Android locks it for apps installed from a file. Open App info, tap ⋮ (top right), choose \"Allow restricted settings\", then try again.",
            "App info",
        ) { Setup.openAppInfo(c) }
        Hint("Xiaomi, Oppo, Vivo, Realme, Samsung", "Also allow Autostart and set battery use to \"Unrestricted\" for Kilo Notch, or the phone may stop it.", "App info") {
            Setup.openAppInfo(c)
        }
    }
}

@Composable
private fun IslandCard(a: Color, b: Color) {
    val c = LocalContext.current
    Card("Island") {
        Toggle("Kilo when idle", "His eyes sit around your camera: blinking, happy, sleepy", Prefs.showKilo, a) { Prefs.showKilo = it }
        Toggle("Notifications", "New notifications pop open the island", Prefs.notifications, a) { Prefs.notifications = it }
        Toggle("Private on lock screen", "Only the app name while the phone is locked", Prefs.privateOnLock, a) { Prefs.privateOnLock = it }
        Toggle("Music", "Cover, sound bars and controls for what's playing", Prefs.music, a) { Prefs.music = it }
        Toggle("Album colors", "The seek bar and bars take the cover's colors", Prefs.albumColors, a) { Prefs.albumColors = it }
        Toggle("Hide in landscape", null, Prefs.hideLandscape, a) { Prefs.hideLandscape = it }
        Toggle("Hide in full-screen apps", "Videos and games", Prefs.hideFullscreen, a) { Prefs.hideFullscreen = it }
        Label("Fit it to your camera")
        Range("Size", Prefs.size, 0.8f..1.3f, 9, "${(Prefs.size * 100).roundToInt()}%", a) { Prefs.size = it }
        Range("Up / down", Prefs.offsetY, -12f..12f, 23, "${Prefs.offsetY.roundToInt()} dp", a) { Prefs.offsetY = it }
        Range("Left / right", Prefs.offsetX, -40f..40f, 79, "${Prefs.offsetX.roundToInt()} dp", a) { Prefs.offsetX = it }
        Row(Modifier.padding(top = 6.dp)) {
            Pill("Try a notification", icon = Icons.bell) {
                Hub.peek(Peek(Peek.Kind.Notification, "Kilo", "Hi! This is how notifications look.", icon = NotifService.icon(c, c.packageName)))
            }
            Spacer(Modifier.width(8.dp))
            Pill("Open it", brush = Brush.horizontalGradient(listOf(a, b))) { Hub.expanded.value = true }
        }
    }
}

@Composable
private fun ColorsCard(a: Color, b: Color) {
    Card("Colors") {
        Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), horizontalArrangement = Arrangement.SpaceBetween) {
            Kolors.presets.forEach { p ->
                val (pa, pb) = Kolors.accent(p.hue, p.spread, p.sat)
                val on = Prefs.hue == p.hue && Prefs.spread == p.spread && Prefs.sat == p.sat
                Box(
                    Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(Brush.linearGradient(listOf(pa.color(), pb.color())))
                        .border(2.dp, if (on) Color.White else Color.Transparent, RoundedCornerShape(12.dp))
                        .clickable { Prefs.hue = p.hue; Prefs.spread = p.spread; Prefs.sat = p.sat },
                )
            }
        }
        Range("Color", Prefs.hue, 0f..360f, 0, "${Prefs.hue.roundToInt()}°", a) { Prefs.hue = it }
        Range("Gradient width", Prefs.spread, 0f..180f, 0, "${Prefs.spread.roundToInt()}°", a) { Prefs.spread = it }
        Range("Vibrance", Prefs.sat, 0f..1f, 0, "${(Prefs.sat * 100).roundToInt()}%", a) { Prefs.sat = it }
    }
}

@Composable
private fun FocusCard(a: Color, b: Color) {
    val c = LocalContext.current
    val f by Focus.state.collectAsState()
    Card("Focus timer") {
        Range("Focus", Prefs.focusMin.toFloat(), 5f..90f, 16, "${Prefs.focusMin} min", a) { Prefs.focusMin = it.roundToInt(); if (!f.running) Focus.publish(c) }
        Range("Break", Prefs.breakMin.toFloat(), 1f..30f, 28, "${Prefs.breakMin} min", a) { Prefs.breakMin = it.roundToInt(); if (!f.running) Focus.publish(c) }
        Row(Modifier.padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Pill(if (f.running) "Pause" else "Start", brush = Brush.horizontalGradient(listOf(a, b)), icon = if (f.running) Icons.pause else Icons.play) { Focus.toggle(c) }
            Spacer(Modifier.width(8.dp))
            Pill("Reset", icon = Icons.reset) { Focus.reset(c) }
            Spacer(Modifier.width(12.dp))
            Text("${if (f.isBreak) "Break" else "Focus"} · ${clock(focusLeft(f))}", color = Dim, fontSize = 13.sp, style = Tabular)
        }
    }
}

@Composable
private fun WaterCard(a: Color, b: Color) {
    val c = LocalContext.current
    val total by Water.total.collectAsState()
    fun set(block: () -> Unit) { block(); Water.schedule(c) }
    Card("Water") {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(56.dp), contentAlignment = Alignment.Center) {
                Ring(total.toFloat() / Prefs.waterGoal, a, b, Modifier.size(56.dp), 5.dp)
                androidx.compose.material3.Icon(Icons.drop, null, Modifier.size(20.dp), tint = a)
            }
            Column(Modifier.weight(1f).padding(start = 14.dp)) {
                Text("%.2f / %.2f L today".format(total / 1000f, Prefs.waterGoal / 1000f), color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                Text(waterLine(total, Prefs.waterGoal), color = Dim, fontSize = 12.sp)
            }
        }
        Row(Modifier.padding(vertical = 8.dp)) {
            Pill("Drink ${Prefs.waterCup} ml", brush = Brush.horizontalGradient(listOf(a, b)), icon = Icons.add) { Water.add(c) }
            Spacer(Modifier.width(8.dp))
            Pill("Undo", icon = Icons.undo) { Water.undo(c) }
            Spacer(Modifier.width(8.dp))
            Pill("Remind me now", icon = Icons.bell) { Water.remind(c); Water.schedule(c) }
        }
        Toggle("Reminders", "On time with the app closed, after a reboot, in Doze", Prefs.waterOn, a) { set { Prefs.waterOn = it } }
        Range("Daily goal", Prefs.waterGoal.toFloat(), 500f..4000f, 13, "%.2f L".format(Prefs.waterGoal / 1000f), a) { set { Prefs.waterGoal = (it / 250).roundToInt() * 250 } }
        Range("Glass", Prefs.waterCup.toFloat(), 100f..600f, 9, "${Prefs.waterCup} ml", a) { set { Prefs.waterCup = (it / 50).roundToInt() * 50 } }
        Range("Remind every", Prefs.waterEvery.toFloat(), 15f..180f, 10, "${Prefs.waterEvery} min", a) { set { Prefs.waterEvery = (it / 15).roundToInt() * 15 } }
        Range("From", Prefs.waterFrom.toFloat(), 5f..12f, 6, "%02d:00".format(Prefs.waterFrom), a) { set { Prefs.waterFrom = it.roundToInt() } }
        Range("Until", Prefs.waterTo.toFloat(), 15f..24f, 8, "%02d:00".format(Prefs.waterTo), a) { set { Prefs.waterTo = it.roundToInt() } }
    }
}

@Composable
private fun AppsCard(c: Context, a: Color) {
    val apps = Prefs.seenApps.map { it to NotifService.label(c, it) }.sortedBy { it.second.lowercase() }
    Card("Apps in the island") {
        if (apps.isEmpty()) Text("Apps show up here after their first notification. Switch one off to keep it out of the island.", color = Dim, fontSize = 13.sp)
        apps.forEach { (pkg, name) ->
            Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                NotifService.icon(c, pkg)?.let { Image(it.asImageBitmap(), null, Modifier.size(28.dp).clip(RoundedCornerShape(8.dp))) }
                Text(name, color = Color.White, fontSize = 14.sp, modifier = Modifier.weight(1f).padding(horizontal = 12.dp))
                Switch(pkg !in Prefs.muted, { Prefs.muted = if (it) Prefs.muted - pkg else Prefs.muted + pkg }, colors = switchColors(a))
            }
        }
    }
}

// ---------- building blocks ----------

@Composable
private fun Card(title: String, content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxWidth().padding(vertical = 7.dp).clip(RoundedCornerShape(22.dp)).background(Color(0xFF0F1118)).padding(18.dp)) {
        Text(title, color = Color.White, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(bottom = 6.dp))
        content()
    }
}

@Composable
private fun Label(text: String) = Text(text, color = Dim, fontSize = 12.sp, fontWeight = FontWeight.Medium, modifier = Modifier.padding(top = 12.dp, bottom = 2.dp))

@Composable
private fun Step(title: String, sub: String, done: Boolean, a: Color, go: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(26.dp).clip(CircleShape).background(if (done) Good else SurfaceHi), contentAlignment = Alignment.Center) {
            if (done) androidx.compose.material3.Icon(Icons.check, null, Modifier.size(16.dp), tint = Color.Black)
        }
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text(title, color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.Medium)
            Text(sub, color = Dim, fontSize = 12.sp, lineHeight = 15.sp)
        }
        if (done) Text("On", color = Good, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
        else Pill("Allow", brush = SolidColor(a.copy(alpha = 0.28f)), onClick = go)
    }
}

@Composable
private fun Hint(title: String, text: String, button: String, go: () -> Unit) {
    Column(Modifier.fillMaxWidth().padding(top = 8.dp).clip(RoundedCornerShape(14.dp)).background(Surface).padding(14.dp)) {
        Text(title, color = Color.White, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
        Text(text, color = Dim, fontSize = 12.sp, lineHeight = 16.sp, modifier = Modifier.padding(vertical = 6.dp))
        Pill(button, onClick = go)
    }
}

@Composable
private fun Toggle(title: String, sub: String?, value: Boolean, a: Color, set: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { set(!value) }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, color = Color.White, fontSize = 15.sp)
            if (sub != null) Text(sub, color = Dim, fontSize = 12.sp, lineHeight = 15.sp)
        }
        Switch(value, set, colors = switchColors(a))
    }
}

@Composable
private fun switchColors(a: Color) = SwitchDefaults.colors(
    checkedThumbColor = Color.White, checkedTrackColor = a, checkedBorderColor = a,
    uncheckedThumbColor = Dim, uncheckedTrackColor = SurfaceHi, uncheckedBorderColor = SurfaceHi,
)

@Composable
private fun Range(title: String, value: Float, range: ClosedFloatingPointRange<Float>, steps: Int, shown: String, a: Color, set: (Float) -> Unit) {
    Column(Modifier.padding(top = 4.dp)) {
        Row {
            Text(title, color = Color.White, fontSize = 14.sp, modifier = Modifier.weight(1f))
            Text(shown, color = Dim, fontSize = 13.sp, style = Tabular)
        }
        Slider(
            value.coerceIn(range), set, valueRange = range, steps = steps,
            colors = SliderDefaults.colors(thumbColor = Color.White, activeTrackColor = a, inactiveTrackColor = SurfaceHi,
                activeTickColor = Color.Transparent, inactiveTickColor = Color.Transparent),
        )
    }
}
