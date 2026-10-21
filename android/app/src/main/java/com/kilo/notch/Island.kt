package com.kilo.notch

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.SizeTransform
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.wrapContentSize
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.StateFlow
import java.time.LocalTime

private enum class Mode { Hidden, Bare, Idle, Compact, Peek, Expanded }
private enum class Act { Focus, Music, Live }

val Tabular = TextStyle(fontFeatureSettings = "tnum")

/**
 * The island. Its size springs between states while the content inside keeps its final size, so
 * nothing reflows mid-animation. It asks the service for a window just big enough to hold it.
 */
@Composable
fun IslandUi(geoFlow: StateFlow<Geo>, resize: (Int, Int) -> Unit, place: (Int) -> Unit) {
    val c = LocalContext.current
    val haptic = LocalHapticFeedback.current
    val d = LocalDensity.current
    val geo by geoFlow.collectAsState()
    Prefs.changed.collectAsState().value // settings apply live
    val peek by Hub.peek.collectAsState()
    val live by Hub.live.collectAsState()
    val expanded by Hub.expanded.collectAsState()
    val music by Music.now.collectAsState()
    val focus by Focus.state.collectAsState()
    val reaction by Hub.reaction.collectAsState()
    val battery by Hub.battery.collectAsState()

    // a peek leaves on its own after a few seconds
    LaunchedEffect(peek?.id) {
        val p = peek ?: return@LaunchedEffect
        delay(p.ms)
        if (Hub.peek.value?.id == p.id) Hub.peek.value = null
    }
    // the expanded island folds back up after a while untouched
    var touched by remember { mutableIntStateOf(0) }
    LaunchedEffect(expanded, touched) {
        if (expanded) { delay(10_000); Hub.expanded.value = false }
    }

    val act = when {
        focus.running -> Act.Focus
        Prefs.music && music?.playing == true -> Act.Music
        live.isNotEmpty() -> Act.Live
        else -> null
    }
    val hidden = (geo.landscape && Prefs.hideLandscape) || (geo.barsHidden && Prefs.hideFullscreen && peek == null) || geo.screenW == 0
    val mode = when {
        hidden -> Mode.Hidden
        peek != null -> Mode.Peek
        expanded -> Mode.Expanded
        act != null -> Mode.Compact
        Prefs.showKilo -> Mode.Idle
        else -> Mode.Bare
    }

    // ---------- geometry ----------
    val s = Prefs.size
    val cam = with(d) { geo.camW.toDp() }
    val gap = if (geo.camW > 0) cam + 10.dp else 18.dp
    val baseH = ((if (geo.camH > 0) with(d) { geo.camH.toDp() } + 10.dp else 30.dp) * s).coerceIn(26.dp, 44.dp)
    val top = (with(d) { geo.camY.toDp() } - baseH / 2 + Prefs.offsetY.dp).coerceAtLeast(0.dp)
    val below = if (geo.camH > 0) (with(d) { (geo.camY + geo.camH / 2).toDp() } - top + 4.dp).coerceIn(10.dp, 30.dp) else 12.dp
    val wide = (with(d) { geo.screenW.toDp() } - 16.dp).coerceAtMost(420.dp)
    fun sizeOf(m: Mode): Pair<Dp, Dp> = when (m) {
        Mode.Hidden -> 0.dp to 0.dp
        Mode.Bare -> gap + 22.dp to baseH
        Mode.Idle -> gap + 56.dp * s to baseH
        Mode.Compact -> gap + 150.dp * s to baseH
        Mode.Peek -> wide to below + 62.dp
        Mode.Expanded -> wide to below + 176.dp
    }
    val (tw, th) = sizeOf(mode)
    val radius = when (mode) { Mode.Peek -> 28.dp; Mode.Expanded -> 36.dp; else -> baseH / 2 }
    val spec = spring<Dp>(dampingRatio = 0.74f, stiffness = 420f)
    val w by animateDpAsState(tw, spec, label = "w")
    val h by animateDpAsState(th, spec, label = "h")
    val r by animateDpAsState(radius, spec, label = "r")

    LaunchedEffect(tw, th, top) {
        with(d) { resize((tw + 4.dp).roundToPx(), if (mode == Mode.Hidden) 1 else (top + th + 6.dp).roundToPx()) }
    }
    LaunchedEffect(geo.camX, Prefs.offsetX) { place(geo.camX + with(d) { Prefs.offsetX.dp.roundToPx() }) }
    LaunchedEffect(mode == Mode.Hidden) { Hub.showing.value = mode != Mode.Hidden }
    if (mode == Mode.Hidden && w < 1.dp) return

    // ---------- colors and mood ----------
    val (aI, bI) = music?.colors?.takeIf { Prefs.albumColors && act == Act.Music } ?: Kolors.accent()
    val a = aI.color()
    val b = bI.color()
    val glow by animateFloatAsState(
        when (mode) { Mode.Idle -> 0.5f; Mode.Compact -> 0.2f; Mode.Peek -> 0.32f; Mode.Expanded -> 0.34f; else -> 0f },
        tween(500), label = "glow",
    )
    val clockTick by produceState(0L) { while (true) { value = System.currentTimeMillis(); delay(30_000) } }
    val mood = run {
        val now = maxOf(clockTick, System.currentTimeMillis())
        val hour = LocalTime.now().hour
        when {
            reaction.second > now -> reaction.first
            hour >= 23 || hour < 6 -> Mood.Asleep
            Prefs.thirsty || (battery.first <= 20 && !battery.second) -> Mood.Sad
            else -> Mood.Normal
        }
    }
    LaunchedEffect(reaction) { // come back from a passing mood on time
        val left = reaction.second - System.currentTimeMillis()
        if (left > 0) { delay(left + 50); Hub.reaction.value = Mood.Normal to 0L }
    }

    // ---------- the island ----------
    Box(Modifier.fillMaxSize()) {
        Box(
            Modifier.align(Alignment.TopCenter).padding(top = top).size(w, h).clip(RoundedCornerShape(r))
                .drawBehind {
                    drawRect(Color.Black)
                    if (glow > 0.01f) {
                        drawRect(Brush.horizontalGradient(listOf(a, b)), alpha = glow)
                        drawRect(Brush.verticalGradient(0f to Color.Black, 0.5f to Color.Black.copy(alpha = 0.7f), 1f to Color.Transparent))
                    }
                }
                .pointerInput(Unit) {
                    awaitPointerEventScope { while (true) { awaitPointerEvent(PointerEventPass.Initial); touched++ } }
                }
                .pointerInput(mode, peek?.id) {
                    detectTapGestures(
                        onTap = {
                            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            when (mode) {
                                Mode.Peek -> {
                                    val p = peek
                                    Hub.peek.value = null
                                    when (p?.kind) {
                                        Peek.Kind.Notification -> open(c, p.tap)
                                        Peek.Kind.Water, Peek.Kind.Focus -> Hub.expanded.value = true
                                        else -> {}
                                    }
                                }
                                Mode.Expanded -> {}
                                else -> Hub.expanded.value = true
                            }
                        },
                        onLongPress = {
                            haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                            Hub.expanded.value = false
                            openSettings(c)
                        },
                    )
                }
                .pointerInput(mode) {
                    var pull = 0f
                    detectVerticalDragGestures(
                        onDragStart = { pull = 0f },
                        onDragEnd = {
                            if (pull < -30f) { Hub.peek.value = null; Hub.expanded.value = false }
                            else if (pull > 30f && mode != Mode.Expanded) { Hub.peek.value = null; Hub.expanded.value = true }
                        },
                    ) { _, dy -> pull += dy }
                },
        ) {
            AnimatedContent(
                targetState = mode,
                transitionSpec = {
                    (fadeIn(tween(220, 90)) + scaleIn(tween(320, 60), initialScale = 0.9f))
                        .togetherWith(fadeOut(tween(90))).using(SizeTransform(clip = false))
                },
                contentAlignment = Alignment.TopCenter,
                label = "content",
            ) { m ->
                val (cw, ch) = sizeOf(m)
                Box(Modifier.wrapContentSize(Alignment.TopCenter, unbounded = true).requiredSize(cw, ch)) {
                    when (m) {
                        Mode.Idle -> Kilo(mood, gap, Modifier.align(Alignment.Center), scale = s)
                        Mode.Compact -> Compact(act, gap, a, b, music, focus, live)
                        Mode.Peek -> peek?.let { PeekView(it, below, a, b) }
                        Mode.Expanded -> Expanded(below, a, b, music, focus, act)
                        else -> {}
                    }
                }
            }
        }
    }
}

@Composable
private fun Compact(act: Act?, gap: Dp, a: Color, b: Color, music: Music.Now?, focus: Focus.State, live: List<Live>) {
    Row(Modifier.fillMaxSize().padding(horizontal = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
            when (act) {
                Act.Music -> Art(music?.art, 22.dp, 7.dp)
                Act.Focus -> Box(Modifier.size(20.dp), contentAlignment = Alignment.Center) {
                    val left = focusLeft(focus)
                    Ring(left.toFloat() / focus.total, a, b, Modifier.size(20.dp), 2.6.dp)
                }
                Act.Live -> live.lastOrNull()?.icon?.let { Image(it.asImageBitmap(), null, Modifier.size(20.dp).clip(CircleShape)) }
                null -> {}
            }
        }
        Spacer(Modifier.width(gap))
        Box(Modifier.weight(1f), contentAlignment = Alignment.CenterEnd) {
            when (act) {
                Act.Music -> Bars(a, b, music?.playing == true, Modifier.size(20.dp, 15.dp))
                Act.Focus -> Text(clock(focusLeft(focus)), color = a, fontSize = 14.sp, fontWeight = FontWeight.Bold, style = Tabular)
                Act.Live -> live.lastOrNull()?.let { Text(chrono(it), color = a, fontSize = 14.sp, fontWeight = FontWeight.Bold, style = Tabular) }
                null -> {}
            }
        }
    }
}

/** The focus countdown, ticking while it runs. */
@Composable
fun focusLeft(f: Focus.State): Long {
    val left by produceState(f.left, f) {
        value = Focus.left()
        while (f.running) { delay(250); value = Focus.left() }
    }
    return left
}

@Composable
private fun chrono(l: Live): String {
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(l.key) { while (true) { delay(500); now = System.currentTimeMillis() } }
    val ms = if (l.countDown) l.base - now else now - l.base
    val t = maxOf(0, ms / 1000)
    return if (t >= 3600) "%d:%02d:%02d".format(t / 3600, t / 60 % 60, t % 60) else "%d:%02d".format(t / 60, t % 60)
}

@Composable
private fun PeekView(p: Peek, below: Dp, a: Color, b: Color) {
    val c = LocalContext.current
    Row(Modifier.fillMaxSize().padding(start = 14.dp, end = 14.dp, top = below, bottom = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(44.dp), contentAlignment = Alignment.Center) {
            when (p.kind) {
                Peek.Kind.Notification -> {
                    val pic = p.avatar ?: p.icon
                    if (pic != null) Image(pic.asImageBitmap(), null, Modifier.size(44.dp).clip(RoundedCornerShape(if (p.avatar != null) 22.dp else 12.dp)))
                    else Icon(Icons.bell, null, Modifier.size(24.dp), tint = a)
                }
                Peek.Kind.Water -> { Ring(p.progress ?: 0f, a, b, Modifier.size(42.dp), 3.5.dp); Icon(Icons.drop, null, Modifier.size(18.dp), tint = a) }
                Peek.Kind.Focus -> { Ring(1f, a, b, Modifier.size(42.dp), 3.5.dp); Icon(Icons.check, null, Modifier.size(20.dp), tint = Color.White) }
                Peek.Kind.Charging -> { Ring(p.progress ?: 0f, Good, Good, Modifier.size(42.dp), 3.5.dp); Icon(Icons.bolt, null, Modifier.size(18.dp), tint = Good) }
                Peek.Kind.Headphones -> Box(Modifier.size(42.dp).clip(RoundedCornerShape(12.dp)).background(Surface), contentAlignment = Alignment.Center) {
                    Icon(Icons.headphones, null, Modifier.size(20.dp), tint = a)
                }
            }
        }
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text(p.title, color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(p.text, color = Dim, fontSize = 13.sp, maxLines = 2, overflow = TextOverflow.Ellipsis, lineHeight = 16.sp)
        }
        when (p.kind) {
            Peek.Kind.Notification -> if (p.avatar != null && p.icon != null) Image(p.icon.asImageBitmap(), null, Modifier.size(18.dp).clip(RoundedCornerShape(5.dp)))
            Peek.Kind.Water -> Pill("Drink", brush = Brush.horizontalGradient(listOf(a, b)), icon = Icons.add) { Water.add(c) }
            Peek.Kind.Focus -> Pill(if (Prefs.focusBreak) "Break" else "Focus", brush = Brush.horizontalGradient(listOf(a, b)), icon = Icons.play) {
                Hub.peek.value = null
                Focus.toggle(c)
            }
            Peek.Kind.Charging -> Text(p.text, color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.Light)
            Peek.Kind.Headphones -> {}
        }
    }
}

@Composable
private fun Expanded(below: Dp, a: Color, b: Color, music: Music.Now?, focus: Focus.State, act: Act?) {
    val start = when { act == Act.Focus || focus.running -> 1; music != null -> 0; else -> 2 }
    Column(Modifier.fillMaxSize().padding(top = below, bottom = 8.dp)) {
        Pages(start, Modifier.weight(1f)) { page ->
            when (page) {
                0 -> MusicPage(music, a, b)
                1 -> FocusPage(focus, a, b)
                else -> WaterPage(a, b)
            }
        }
    }
}

@Composable
fun Dots(count: Int, current: Int, modifier: Modifier = Modifier) {
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        repeat(count) { i ->
            val wd by animateDpAsState(if (i == current) 16.dp else 5.dp, spring(dampingRatio = 0.6f), label = "dot")
            Box(Modifier.padding(horizontal = 2.5.dp).size(wd, 5.dp).clip(CircleShape).background(if (i == current) Color.White else SurfaceHi))
        }
    }
}
