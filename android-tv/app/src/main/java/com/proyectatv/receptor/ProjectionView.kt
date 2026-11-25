package com.proyectatv.receptor

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Shader
import android.view.View

/**
 * Dibuja el cuadro recibido girado según la posición física del TV.
 * rotationQ: 0 = normal, 1 = 90° horario, 2 = 180°, 3 = 90° antihorario.
 */
class ProjectionView(context: Context) : View(context) {

    private var bmp: Bitmap? = null

    var rotationQ: Int = 1
        set(value) {
            field = ((value % 4) + 4) % 4
            updateEffective()
            invalidate()
        }

    /** Líneas de ayuda cuando no hay imagen. Las que empiezan con "~" salen en gris. */
    var info: List<String> = emptyList()
    var showInfo: Boolean = true

    /** Tamaño real que el usuario ve (ya con el giro aplicado). */
    @Volatile var effW: Int = 1080
    @Volatile var effH: Int = 1920

    var onLayoutChanged: (() -> Unit)? = null

    private val paint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textAlign = Paint.Align.CENTER }
    private val dst = RectF()

    private fun updateEffective() {
        if (width == 0 || height == 0) return
        if (rotationQ % 2 == 1) {
            effW = height
            effH = width
        } else {
            effW = width
            effH = height
        }
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        updateEffective()
        onLayoutChanged?.invoke()
    }

    fun setFrame(b: Bitmap?) {
        bmp = b
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        canvas.drawColor(Color.BLACK)
        val w = width.toFloat()
        val h = height.toFloat()

        canvas.save()
        when (rotationQ) {
            1 -> { canvas.translate(w, 0f); canvas.rotate(90f); dst.set(0f, 0f, h, w) }
            2 -> { canvas.translate(w, h); canvas.rotate(180f); dst.set(0f, 0f, w, h) }
            3 -> { canvas.translate(0f, h); canvas.rotate(270f); dst.set(0f, 0f, h, w) }
            else -> dst.set(0f, 0f, w, h)
        }

        val b = bmp
        if (b != null && !showInfo) canvas.drawBitmap(b, null, dst, paint) else drawInfo(canvas)
        canvas.restore()
    }

    private fun drawInfo(c: Canvas) {
        val ew = dst.width()
        val eh = dst.height()

        paint.shader = LinearGradient(0f, 0f, 0f, eh, 0xFF0A0E22.toInt(), 0xFF2A1048.toInt(), Shader.TileMode.CLAMP)
        c.drawRect(dst, paint)
        paint.shader = null

        val u = minOf(ew, eh) / 24f
        var y = eh * 0.28f

        textPaint.isFakeBoldText = true
        textPaint.color = 0xFF00E5FF.toInt()
        textPaint.textSize = u * 2.4f
        c.drawText("ProyectaTV", ew / 2f, y, textPaint)
        y += u * 2.2f

        textPaint.isFakeBoldText = false
        textPaint.textSize = u * 1.05f
        for (line in info) {
            val grey = line.startsWith("~")
            textPaint.color = if (grey) 0xFF8D97C4.toInt() else Color.WHITE
            c.drawText(if (grey) line.substring(1) else line, ew / 2f, y, textPaint)
            y += u * 1.7f
        }
    }
}
