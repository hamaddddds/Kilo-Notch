package com.kilo.notch

import android.app.ActivityOptions
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.os.Build
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlin.math.abs
import kotlin.math.sin
import kotlin.random.Random

val Dim = Color(0xFF8E8E96)
val Surface = Color(0xFF1E1E22)
val SurfaceHi = Color(0xFF2E2E35)
val Good = Color(0xFF30D158)

fun Int.color() = Color(this)

/** Kilo's two eyes with the camera between them. Blinks and glances around on his own while calm. */
@Composable
fun Kilo(mood: Mood, gap: Dp, modifier: Modifier = Modifier, scale: Float = 1f) {
    val blink = remember { Animatable(1f) }
    val look = remember { Animatable(0f) }
    val hop = remember { Animatable(0f) }
    LaunchedEffect(mood) {
        if (mood == Mood.Happy) {
            hop.animateTo(-3.5f, tween(110))
            hop.animateTo(0f, spring(dampingRatio = 0.35f, stiffness = 320f))
        }
    }
    LaunchedEffect(mood) {
        if (mood != Mood.Normal) return@LaunchedEffect
        while (true) {
            delay(Random.nextLong(2200, 6000))
            repeat(if (Random.nextInt(5) == 0) 2 else 1) { // sometimes a double blink
                blink.animateTo(0.12f, tween(70))
                blink.animateTo(1f, tween(120))
            }
            if (Random.nextInt(3) == 0) look.animateTo(Random.nextInt(-1, 2) * 2.2f, spring(dampingRatio = 0.6f, stiffness = 110f))
        }
    }
    // the tear and the z drift on a loop, only in the moods that have them
    val drift = if (mood == Mood.Sad || mood == Mood.Asleep) {
        val t = rememberInfiniteTransition(label = "drift")
        t.animateFloat(0f, 1f, infiniteRepeatable(tween(1900, easing = LinearEasing), RepeatMode.Restart), label = "d").value
    } else 0f
    Row(modifier.offset(y = hop.value.dp), verticalAlignment = Alignment.CenterVertically) {
        Eye(mood, left = true, blink.value, look.value, drift, scale)
        Spacer(Modifier.width(gap))
        Eye(mood, left = false, blink.value, look.value, drift, scale)
    }
}

@Composable
private fun Eye(mood: Mood, left: Boolean, blink: Float, look: Float, drift: Float, scale: Float) {
    Canvas(Modifier.size(14.dp * scale, 22.dp * scale)) {
        val u = size.width / 14f
        val cx = size.width / 2 + look * u
        val cy = size.height / 2
        val white = Color.White
        val line = Stroke(width = 2.6f * u, cap = StrokeCap.Round, join = StrokeJoin.Round)
        fun pill(h: Float, dy: Float = 0f) {
            val w = 6.5f * u
            val hh = maxOf(h, 1.6f * u)
            drawRoundRect(white, Offset(cx - w / 2, cy + dy - hh / 2), Size(w, hh), CornerRadius(w / 2))
        }
        fun stroke(vararg pts: Float, curve: Boolean = false, width: Stroke = line, color: Color = white) {
            val p = Path().apply {
                moveTo(cx + pts[0] * u, cy + pts[1] * u)
                if (curve) quadraticTo(cx + pts[2] * u, cy + pts[3] * u, cx + pts[4] * u, cy + pts[5] * u)
                else for (i in 2 until pts.size step 2) lineTo(cx + pts[i] * u, cy + pts[i + 1] * u)
            }
            drawPath(p, color, style = width)
        }
        when (mood) {
            Mood.Normal -> pill(12f * u * blink)
            Mood.Happy -> stroke(-5f, 2.5f, 0f, -7f, 5f, 2.5f, curve = true)
            Mood.Asleep -> {
                stroke(-5f, -1.5f, 0f, 5f, 5f, -1.5f, curve = true)
                if (!left) zee(cx + 6 * u, cy - 6 * u - drift * 6 * u, u, 1 - drift)
            }
            Mood.Wink -> if (left) stroke(-3f, -5f, 3f, 0f, -3f, 5f) else pill(12f * u)
            Mood.Sad -> {
                val brow = Stroke(width = 2f * u, cap = StrokeCap.Round)
                if (left) stroke(-4f, -6f, 4f, -8.5f, width = brow) else stroke(-4f, -8.5f, 4f, -6f, width = brow)
                pill(9f * u, dy = 1.5f * u)
                if (left) drawCircle(Color(0xFF8FD3FF).copy(alpha = 1 - drift), 1.7f * u, Offset(cx - 1.5f * u, cy + 7.5f * u + drift * 4 * u))
            }
        }
    }
}

private fun DrawScope.zee(x: Float, y: Float, u: Float, alpha: Float) {
    val p = Path().apply { moveTo(x, y); lineTo(x + 3.5f * u, y); lineTo(x, y + 3.5f * u); lineTo(x + 3.5f * u, y + 3.5f * u) }
    drawPath(p, Color.White.copy(alpha = alpha.coerceIn(0f, 1f)), style = Stroke(width = 1.4f * u, cap = StrokeCap.Round, join = StrokeJoin.Round))
}

/** Progress ring: a faint full circle with the value on top, clockwise from 12 o'clock. */
@Composable
fun Ring(value: Float, a: Color, b: Color, modifier: Modifier, thickness: Dp = 3.dp) {
    Canvas(modifier) {
        val t = thickness.toPx()
        val inset = t / 2
        val box = Size(size.width - t, size.height - t)
        drawArc(a.copy(alpha = 0.22f), 0f, 360f, false, Offset(inset, inset), box, style = Stroke(t))
        val v = value.coerceIn(0f, 1f)
        if (v > 0.001f) drawArc(Brush.sweepGradient(listOf(a, b, a)), -90f, 360f * v, false, Offset(inset, inset), box, style = Stroke(t, cap = StrokeCap.Round))
    }
}

/** Little equalizer bars that dance while music plays (a gradient runs across all five). */
@Composable
fun Bars(a: Color, b: Color, playing: Boolean, modifier: Modifier) {
    var phase by remember { mutableFloatStateOf(0f) }
    LaunchedEffect(playing) {
        while (playing) withFrameNanos { phase = it / 1_000_000_000f * 6.5f }
    }
    Canvas(modifier) {
        val n = 5
        val w = size.width / (n * 2 - 1)
        val brush = Brush.horizontalGradient(listOf(a, b), 0f, size.width)
        for (i in 0 until n) {
            val k = if (playing) 0.3f + 0.7f * abs(sin(phase * (0.9f + i * 0.23f) + i * 1.9f)) else 0.3f
            val h = (size.height * k).coerceAtLeast(w)
            drawRoundRect(brush, Offset(i * 2 * w, (size.height - h) / 2), Size(w, h), CornerRadius(w / 2))
        }
    }
}

@Composable
fun Art(art: Bitmap?, size: Dp, radius: Dp) {
    Box(Modifier.size(size).clip(RoundedCornerShape(radius)).background(Surface), contentAlignment = Alignment.Center) {
        if (art != null) Image(art.asImageBitmap(), null, Modifier.size(size), contentScale = ContentScale.Crop)
        else Icon(Icons.note, null, Modifier.size(size * 0.45f), tint = Dim)
    }
}

/** A rounded button that dips when pressed. */
@Composable
fun Pill(text: String, modifier: Modifier = Modifier, brush: Brush = SolidColor(Surface), fg: Color = Color.White, icon: ImageVector? = null, onClick: () -> Unit) {
    val press = remember { MutableInteractionSource() }
    val down by press.collectIsPressedAsState()
    Row(
        modifier.scale(if (down) 0.92f else 1f).clip(RoundedCornerShape(12.dp)).background(brush)
            .clickable(press, null, onClick = onClick).padding(horizontal = 13.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) Icon(icon, null, Modifier.size(16.dp), tint = fg)
        if (icon != null && text.isNotEmpty()) Spacer(Modifier.width(6.dp))
        if (text.isNotEmpty()) Text(text, color = fg, fontSize = 13.sp, fontWeight = FontWeight.Medium)
    }
}

@Composable
fun Tap(icon: ImageVector, size: Dp, modifier: Modifier = Modifier, tint: Color = Color.White, onClick: () -> Unit) {
    val press = remember { MutableInteractionSource() }
    val down by press.collectIsPressedAsState()
    Box(modifier.scale(if (down) 0.85f else 1f).clip(RoundedCornerShape(12.dp)).clickable(press, null, onClick = onClick).padding(8.dp)) {
        Icon(icon, null, Modifier.size(size), tint = tint)
    }
}

/** Opens a notification's own target, the way tapping it in the shade would. */
fun open(c: Context, pi: PendingIntent?) {
    if (pi == null) return
    try {
        if (Build.VERSION.SDK_INT >= 34) {
            @Suppress("DEPRECATION")
            val o = ActivityOptions.makeBasic().setPendingIntentBackgroundActivityStartMode(ActivityOptions.MODE_BACKGROUND_ACTIVITY_START_ALLOWED)
            pi.send(c, 0, null, null, null, null, o.toBundle())
        } else pi.send()
    } catch (e: PendingIntent.CanceledException) { /* the app withdrew it */ }
}

fun openSettings(c: Context) = c.startActivity(Intent(c, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))

/** Material icon paths, drawn as vectors (no icon library needed). */
object Icons {
    private fun v(d: String) = ImageVector.Builder(defaultWidth = 24.dp, defaultHeight = 24.dp, viewportWidth = 24f, viewportHeight = 24f)
        .addPath(addPathNodes(d), fill = SolidColor(Color.White)).build()

    val play = v("M8,5v14l11,-7z")
    val pause = v("M6,19h4V5H6v14zm8,-14v14h4V5h-4z")
    val next = v("M6,18l8.5,-6L6,6v12zM16,6v12h2V6h-2z")
    val prev = v("M6,6h2v12H6zm3.5,6l8.5,6V6z")
    val drop = v("M12,2c-5.33,4.55 -8,8.48 -8,11.8c0,4.98 3.8,8.2 8,8.2s8,-3.22 8,-8.2c0,-3.32 -2.67,-7.25 -8,-11.8z")
    val timer = v("M15,1H9v2h6V1zm-4,13h2V8h-2v6zm8.03,-6.61l1.42,-1.42c-0.43,-0.51 -0.9,-0.99 -1.41,-1.41l-1.42,1.42C16.07,4.74 14.12,4 12,4c-4.97,0 -9,4.03 -9,9s4.02,9 9,9 9,-4.03 9,-9c0,-2.12 -0.74,-4.07 -1.97,-5.61zM12,20c-3.87,0 -7,-3.13 -7,-7s3.13,-7 7,-7 7,3.13 7,7 -3.13,7 -7,7z")
    val coffee = v("M20,3H4v10c0,2.21 1.79,4 4,4h6c2.21,0 4,-1.79 4,-4v-3h2c1.11,0 2,-0.89 2,-2V5c0,-1.11 -0.89,-2 -2,-2zm0,5h-2V5h2v3zM4,19h16v2H4z")
    val reset = v("M17.65,6.35C16.2,4.9 14.21,4 12,4c-4.42,0 -7.99,3.58 -7.99,8s3.57,8 7.99,8c3.73,0 6.84,-2.55 7.73,-6h-2.08c-0.82,2.33 -3.04,4 -5.65,4 -3.31,0 -6,-2.69 -6,-6s2.69,-6 6,-6c1.66,0 3.14,0.69 4.22,1.78L13,11h7V4l-2.35,2.35z")
    val check = v("M9,16.17L4.83,12l-1.42,1.41L9,19 21,7l-1.41,-1.41z")
    val add = v("M19,13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z")
    val undo = v("M12.5,8c-2.65,0 -5.05,0.99 -6.9,2.6L2,7v9h9l-3.62,-3.62c1.39,-1.16 3.16,-1.88 5.12,-1.88 3.54,0 6.55,2.31 7.6,5.5l2.37,-0.78C21.08,11.03 17.15,8 12.5,8z")
    val bell = v("M12,22c1.1,0 2,-0.9 2,-2h-4c0,1.1 0.89,2 2,2zm6,-6v-5c0,-3.07 -1.64,-5.64 -4.5,-6.32V4c0,-0.83 -0.67,-1.5 -1.5,-1.5s-1.5,0.67 -1.5,1.5v0.68C7.63,5.36 6,7.92 6,11v5l-2,2v1h16v-1l-2,-2z")
    val note = v("M12,3v10.55c-0.59,-0.34 -1.27,-0.55 -2,-0.55 -2.21,0 -4,1.79 -4,4s1.79,4 4,4 4,-1.79 4,-4V7h4V3h-6z")
    val bolt = v("M7,2v11h3v9l7,-12h-4l4,-8z")
    val headphones = v("M12,3c-4.97,0 -9,4.03 -9,9v7c0,1.1 0.9,2 2,2h3v-8H5v-1c0,-3.87 3.13,-7 7,-7s7,3.13 7,7v1h-3v8h3c1.1,0 2,-0.9 2,-2v-7c0,-4.97 -4.03,-9 -9,-9z")
    val settings = v("M19.14,12.94c0.04,-0.3 0.06,-0.61 0.06,-0.94c0,-0.32 -0.02,-0.64 -0.07,-0.94l2.03,-1.58c0.18,-0.14 0.23,-0.41 0.12,-0.61l-1.92,-3.32c-0.12,-0.22 -0.37,-0.29 -0.59,-0.22l-2.39,0.96c-0.5,-0.38 -1.03,-0.7 -1.62,-0.94L14.4,2.81c-0.04,-0.24 -0.24,-0.41 -0.48,-0.41h-3.84c-0.24,0 -0.43,0.17 -0.47,0.41L9.25,5.35C8.66,5.59 8.12,5.92 7.63,6.29L5.24,5.33c-0.22,-0.08 -0.47,0 -0.59,0.22L2.74,8.87C2.62,9.08 2.66,9.34 2.86,9.48l2.03,1.58C4.84,11.36 4.8,11.69 4.8,12s0.02,0.64 0.07,0.94l-2.03,1.58c-0.18,0.14 -0.23,0.41 -0.12,0.61l1.92,3.32c0.12,0.22 0.37,0.29 0.59,0.22l2.39,-0.96c0.5,0.38 1.03,0.7 1.62,0.94l0.36,2.54c0.05,0.24 0.24,0.41 0.48,0.41h3.84c0.24,0 0.44,-0.17 0.47,-0.41l0.36,-2.54c0.59,-0.24 1.13,-0.56 1.62,-0.94l2.39,0.96c0.22,0.08 0.47,0 0.59,-0.22l1.92,-3.32c0.12,-0.22 0.07,-0.47 -0.12,-0.61L19.14,12.94zM12,15.6c-1.98,0 -3.6,-1.62 -3.6,-3.6s1.62,-3.6 3.6,-3.6s3.6,1.62 3.6,3.6S13.98,15.6 12,15.6z")
}
