package com.kilo.notch

import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.roundToInt
import kotlin.math.sin

/** Theme colors (the desktop app's presets) and the album palette. Colors are ARGB ints. */
object Kolors {
    data class Preset(val name: String, val hue: Float, val spread: Float, val sat: Float)

    val presets = listOf(
        Preset("Purple", 265f, 50f, 0.9f), Preset("Ocean", 195f, 45f, 0.9f), Preset("Sunset", 335f, 55f, 0.95f),
        Preset("Mint", 150f, 40f, 0.75f), Preset("Aurora", 140f, 150f, 0.85f), Preset("Mono", 0f, 0f, 0f),
    )

    fun accent(hue: Float = Prefs.hue, spread: Float = Prefs.spread, sat: Float = Prefs.sat): Pair<Int, Int> {
        val mono = sat < 0.05f
        return hsl(hue, sat, if (mono) 0.92f else 0.64f) to hsl(hue + spread, sat, if (mono) 0.5f else 0.58f)
    }

    fun hsl(h: Float, s: Float, l: Float): Int {
        val hh = ((h % 360 + 360) % 360) / 360f
        val q = if (l < 0.5f) l * (1 + s) else l + s - l * s
        val p = 2 * l - q
        fun c(t0: Float): Int {
            val t = if (t0 < 0) t0 + 1 else if (t0 > 1) t0 - 1 else t0
            val v = when {
                t < 1 / 6f -> p + (q - p) * 6 * t
                t < 0.5f -> q
                t < 2 / 3f -> p + (q - p) * (2 / 3f - t) * 6
                else -> p
            }
            return (v * 255).roundToInt().coerceIn(0, 255)
        }
        return (0xFF shl 24) or (c(hh + 1 / 3f) shl 16) or (c(hh) shl 8) or c(hh - 1 / 3f)
    }

    /**
     * The cover's two strongest hues. Averaging would turn a rainbow cover grey, so vivid pixels vote
     * into hue buckets; the runner-up must be a different hue or the gradient would be flat.
     */
    fun palette(px: IntArray): Pair<Int, Int> {
        val bins = 24
        val weight = DoubleArray(bins)
        val sx = DoubleArray(bins)
        val sy = DoubleArray(bins)
        val ss = DoubleArray(bins)
        var total = 0.0
        for (p in px) {
            val r = p shr 16 and 255
            val g = p shr 8 and 255
            val b = p and 255
            val max = maxOf(r, g, b).toDouble()
            val d = max - minOf(r, g, b)
            if (max == 0.0) continue
            val h = if (d == 0.0) 0.0 else when (max) {
                r.toDouble() -> 60 * (((g - b) / d) % 6)
                g.toDouble() -> 60 * ((b - r) / d + 2)
                else -> 60 * ((r - g) / d + 4)
            }
            val hue = (h + 360) % 360
            val s = d / max
            val v = max / 255
            val w = s * s * v // vivid, bright pixels count; grey and dark ones barely
            total += v
            val k = (hue / 360 * bins).toInt() % bins
            weight[k] += w
            sx[k] += cos(Math.toRadians(hue)) * w // hue as a vector, so 359° and 1° average to 0°
            sy[k] += sin(Math.toRadians(hue)) * w
            ss[k] += s * w
        }
        fun score(k: Int) = weight[k] + 0.5 * (weight[(k + 1) % bins] + weight[(k + bins - 1) % bins])
        fun hueOf(k: Int) = Math.toDegrees(atan2(sy[k], sx[k])).toFloat()
        fun pick(k: Int): Int {
            val sat = if (weight[k] > 0) ss[k] / weight[k] else 0.0
            return hsl(hueOf(k), (sat * 1.15).coerceIn(0.45, 0.95).toFloat(), 0.64f)
        }
        val first = (0 until bins).maxBy(::score)
        if (weight[first] < total * 0.01) return 0xFFEBEBF0.toInt() to 0xFF9696A0.toInt() // black-and-white cover
        val second = (0 until bins).filter { minOf((it - first + bins) % bins, (first - it + bins) % bins) >= 3 }.maxBy(::score)
        val a = pick(first)
        if (score(second) < score(first) * 0.12) return a to hsl(hueOf(first) + 35, 0.85f, 0.58f) // one-color cover
        return a to pick(second)
    }
}
