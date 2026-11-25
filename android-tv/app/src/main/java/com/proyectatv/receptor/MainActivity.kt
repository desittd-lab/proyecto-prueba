package com.proyectatv.receptor

import android.app.Activity
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.KeyEvent
import android.view.View
import android.view.WindowManager
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class MainActivity : Activity() {

    private lateinit var view: ProjectionView
    private var receiver: Receiver? = null
    private val ui = Handler(Looper.getMainLooper())

    // Doble búfer: "current" se está mostrando, "spare" se reutiliza para decodificar el siguiente.
    private var current: Bitmap? = null
    private var spare: Bitmap? = null
    private var connected = false

    private val deviceName: String
        get() = (Build.MODEL ?: "Android TV").replace("|", " ")

    private val infoTick = object : Runnable {
        override fun run() {
            if (!connected) updateWaitingInfo()
            ui.postDelayed(this, 3000)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        view = ProjectionView(this)
        view.rotationQ = getSharedPreferences("cfg", MODE_PRIVATE).getInt("rot", 1)
        view.onLayoutChanged = { receiver?.sendHelloAsync() }
        setContentView(view)
        hideSystemUi()
        updateWaitingInfo()
    }

    override fun onStart() {
        super.onStart()
        val r = Receiver(
            deviceName = { deviceName },
            helloInfo = { "$deviceName|${view.effW}|${view.effH}|${view.rotationQ}" },
            onConnected = { c -> ui.post { onConnectionChanged(c) } },
            onFrame = { data, len -> handleFrame(data, len) },
            onRotation = { q -> ui.post { setRotation(q) } }
        )
        receiver = r
        r.start()
        ui.removeCallbacks(infoTick)
        ui.post(infoTick)
    }

    override fun onStop() {
        super.onStop()
        ui.removeCallbacks(infoTick)
        receiver?.stop()
        receiver = null
        connected = false
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemUi()
    }

    @Suppress("DEPRECATION")
    private fun hideSystemUi() {
        view.systemUiVisibility = (View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION)
    }

    // OK / ENTER / MENÚ del control remoto: cambia el giro de la imagen.
    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        if (keyCode == KeyEvent.KEYCODE_DPAD_CENTER ||
            keyCode == KeyEvent.KEYCODE_ENTER ||
            keyCode == KeyEvent.KEYCODE_MENU
        ) {
            setRotation((view.rotationQ + 1) % 4)
            return true
        }
        return super.onKeyDown(keyCode, event)
    }

    private fun setRotation(q: Int) {
        view.rotationQ = q
        getSharedPreferences("cfg", MODE_PRIVATE).edit().putInt("rot", view.rotationQ).apply()
        receiver?.sendHelloAsync()
        if (!connected) updateWaitingInfo()
    }

    private fun onConnectionChanged(isConnected: Boolean) {
        connected = isConnected
        if (isConnected) {
            view.info = listOf("Conectado", "~Pulsa Proyectar en tu PC")
            view.showInfo = true
            view.invalidate()
        } else {
            current = null
            spare = null
            view.setFrame(null)
            updateWaitingInfo()
        }
    }

    private fun updateWaitingInfo() {
        val ips = Receiver.localIpv4()
        val deg = view.rotationQ * 90
        view.info = listOf(
            "Esperando a tu PC con Windows",
            "~Nombre de este TV",
            deviceName,
            "~Dirección IP",
            if (ips.isEmpty()) "Sin red" else ips.joinToString("   "),
            "~En Windows abre ProyectaTV y pulsa Conectar",
            "~OK o MENÚ en el control: girar imagen ($deg°)"
        )
        view.showInfo = true
        view.invalidate()
    }

    /** Se ejecuta en el hilo de red: decodifica el JPEG y espera a que se muestre. */
    private fun handleFrame(data: ByteArray, len: Int) {
        val opts = BitmapFactory.Options()
        opts.inMutable = true
        opts.inPreferredConfig = Bitmap.Config.ARGB_8888
        val reuse = spare
        if (reuse != null && reuse.isMutable) opts.inBitmap = reuse

        var bmp: Bitmap? = try {
            BitmapFactory.decodeByteArray(data, 0, len, opts)
        } catch (e: Exception) {
            null
        }
        if (bmp == null) {
            opts.inBitmap = null
            bmp = try {
                BitmapFactory.decodeByteArray(data, 0, len, opts)
            } catch (e: Exception) {
                null
            }
        }
        val decoded = bmp ?: return

        val latch = CountDownLatch(1)
        runOnUiThread {
            val old = current
            current = decoded
            spare = if (old === decoded) null else old
            view.showInfo = false
            view.setFrame(decoded)
            latch.countDown()
        }
        latch.await(500, TimeUnit.MILLISECONDS)
    }
}
