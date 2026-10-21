package com.kilo.notch

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/** Swipe between Music, Focus and Water; dots underneath. */
@Composable
fun Pages(start: Int, modifier: Modifier, page: @Composable (Int) -> Unit) {
    val state = rememberPagerState(start) { 3 }
    Column(modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        HorizontalPager(state, Modifier.weight(1f).fillMaxWidth()) { page(it) }
        Dots(3, state.currentPage, Modifier.padding(top = 4.dp))
    }
}

@Composable
fun MusicPage(m: Music.Now?, a: Color, b: Color) {
    if (m == null) {
        Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
            Kilo(Mood.Normal, 14.dp)
            Gap(10.dp)
            Text("Nothing playing", color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            Text("Play something in Spotify, YouTube Music…", color = Dim, fontSize = 12.sp)
        }
        return
    }
    Column(Modifier.fillMaxSize().padding(horizontal = 20.dp), verticalArrangement = Arrangement.Center) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Art(m.art, 54.dp, 14.dp)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Text(m.title, color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(m.artist, color = Dim, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Bars(a, b, m.playing, Modifier.size(24.dp, 18.dp))
        }
        Gap(8.dp)
        Seek(m, a, b)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
            Tap(Icons.prev, 24.dp) { Music.prev() }
            Tap(if (m.playing) Icons.pause else Icons.play, 32.dp, Modifier.padding(horizontal = 14.dp)) { Music.toggle() }
            Tap(Icons.next, 24.dp) { Music.next() }
        }
    }
}

/** The seek bar: glides with the song, thickens under your finger, seeks where you let go. */
@Composable
private fun Seek(m: Music.Now, a: Color, b: Color) {
    var pos by remember { mutableLongStateOf(m.position()) }
    var drag by remember { mutableStateOf<Float?>(null) }
    LaunchedEffect(m) { while (true) { pos = m.position(); delay(250) } }
    val target = drag ?: if (m.duration > 0) pos.toFloat() / m.duration else 0f
    val shown by animateFloatAsState(target, if (drag != null) snap() else tween(250, easing = LinearEasing), label = "seek")
    val thick by animateDpAsState(if (drag != null) 8.dp else 4.dp, label = "thick")
    val shownPos = drag?.let { (it * m.duration).toLong() } ?: pos
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(if (m.duration > 0) clock(shownPos) else "", color = Dim, fontSize = 11.sp, style = Tabular, modifier = Modifier.width(38.dp))
        Box(
            Modifier.weight(1f).height(24.dp)
                .pointerInput(m.duration) {
                    if (m.duration <= 0) return@pointerInput
                    detectHorizontalDragGestures(
                        onDragStart = { drag = (it.x / size.width).coerceIn(0f, 1f) },
                        onDragEnd = { drag?.let { Music.seek((it * m.duration).toLong()) }; drag = null },
                        onDragCancel = { drag = null },
                    ) { change, _ -> drag = (change.position.x / size.width).coerceIn(0f, 1f) }
                }
                .pointerInput(m.duration) {
                    if (m.duration > 0) detectTapGestures { Music.seek(((it.x / size.width).coerceIn(0f, 1f) * m.duration).toLong()) }
                },
            contentAlignment = Alignment.Center,
        ) {
            Canvas(Modifier.fillMaxWidth().height(thick)) {
                val rr = CornerRadius(size.height / 2)
                drawRoundRect(Color.White.copy(alpha = 0.16f), cornerRadius = rr)
                val fw = size.width * shown.coerceIn(0f, 1f)
                if (fw > 0) drawRoundRect(Brush.horizontalGradient(listOf(a, b), 0f, maxOf(fw, 1f)), Offset.Zero, Size(fw, size.height), rr)
            }
        }
        Text(if (m.duration > 0) "-" + clock(m.duration - shownPos) else "", color = Dim, fontSize = 11.sp, style = Tabular,
            modifier = Modifier.width(42.dp).padding(start = 4.dp))
    }
}

@Composable
fun FocusPage(f: Focus.State, a: Color, b: Color) {
    val c = LocalContext.current
    val left = focusLeft(f)
    Row(Modifier.fillMaxSize().padding(horizontal = 20.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(78.dp), contentAlignment = Alignment.Center) {
            Ring(left.toFloat() / f.total, a, b, Modifier.size(78.dp), 7.dp)
            Icon(if (f.isBreak) Icons.coffee else Icons.timer, null, Modifier.size(26.dp), tint = Color.White)
        }
        Column(Modifier.weight(1f).padding(start = 12.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Text(clock(left), color = Color.White, fontSize = 38.sp, fontWeight = FontWeight.Bold, style = Tabular)
            Text(if (f.isBreak) "Short break" else "Focus time", color = Dim, fontSize = 13.sp)
            Gap(8.dp)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Pill("Break", brush = SolidColor(if (f.isBreak) a.copy(alpha = 0.3f) else Surface)) { Focus.mode(c, true) }
                Spacer(Modifier.width(6.dp))
                Pill("", brush = Brush.horizontalGradient(listOf(a, b)), icon = if (f.running) Icons.pause else Icons.play) { Focus.toggle(c) }
                Spacer(Modifier.width(6.dp))
                Pill("Focus", brush = SolidColor(if (!f.isBreak) a.copy(alpha = 0.3f) else Surface)) { Focus.mode(c, false) }
                Tap(Icons.reset, 16.dp, tint = Dim) { Focus.reset(c) }
            }
        }
    }
}

@Composable
fun WaterPage(a: Color, b: Color) {
    val c = LocalContext.current
    val total by Water.total.collectAsState()
    Prefs.changed.collectAsState().value
    val goal = Prefs.waterGoal
    Row(Modifier.fillMaxSize().padding(horizontal = 20.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(78.dp), contentAlignment = Alignment.Center) {
            Ring(total.toFloat() / goal, a, b, Modifier.size(78.dp), 7.dp)
            Icon(Icons.drop, null, Modifier.size(26.dp), tint = a)
        }
        Column(Modifier.weight(1f).padding(start = 16.dp)) {
            Row(verticalAlignment = Alignment.Bottom) {
                Text("%.2f".format(total / 1000f), color = Color.White, fontSize = 32.sp, fontWeight = FontWeight.Bold, style = Tabular)
                Text(" / %.1f L".format(goal / 1000f), color = Dim, fontSize = 14.sp, modifier = Modifier.padding(bottom = 5.dp))
            }
            Text(waterLine(total, goal), color = Dim, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Gap(10.dp)
            Row {
                Pill("${Prefs.waterCup} ml", brush = Brush.horizontalGradient(listOf(a, b)), icon = Icons.add) { Water.add(c) }
                Spacer(Modifier.width(8.dp))
                Pill("Undo", icon = Icons.undo) { Water.undo(c) }
            }
        }
    }
}

fun waterLine(total: Int, goal: Int): String {
    if (total >= goal) return "Goal reached today"
    val next = Prefs.waterNext
    val at = if (Prefs.waterOn && next > 0) " · next " + DateTimeFormatter.ofPattern("HH:mm").format(Instant.ofEpochMilli(next).atZone(ZoneId.systemDefault())) else ""
    return "${goal - total} ml to go$at"
}

@Composable
fun Gap(h: androidx.compose.ui.unit.Dp) = Spacer(Modifier.height(h))
