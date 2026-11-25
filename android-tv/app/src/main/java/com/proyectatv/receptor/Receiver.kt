package com.proyectatv.receptor

import java.io.BufferedInputStream
import java.io.DataInputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.nio.ByteBuffer
import java.util.Collections
import java.util.concurrent.Executors

/**
 * Servidor de red del TV.
 *  - UDP 50505: anuncia cada 2 s "PROYECTATV1|nombre|puerto" para que Windows lo encuentre.
 *  - TCP 50506: recibe cuadros JPEG. Mensajes: [tipo:1][largo:4 big-endian][datos]
 *      1 HELLO (TV→PC) "nombre|ancho|alto|giro"   2 FRAME (PC→TV) JPEG
 *      3 ROT (PC→TV) 1 byte                        4 ACK (TV→PC) cuadro mostrado
 */
class Receiver(
    private val deviceName: () -> String,
    private val helloInfo: () -> String,
    private val onConnected: (Boolean) -> Unit,
    private val onFrame: (ByteArray, Int) -> Unit,
    private val onRotation: (Int) -> Unit
) {
    companion object {
        const val UDP_PORT = 50505
        const val TCP_PORT = 50506
        const val T_HELLO = 1
        const val T_FRAME = 2
        const val T_ROT = 3
        const val T_ACK = 4

        fun localIpv4(): List<String> {
            val result = ArrayList<String>()
            try {
                for (ni in Collections.list(NetworkInterface.getNetworkInterfaces())) {
                    if (!ni.isUp || ni.isLoopback) continue
                    for (addr in Collections.list(ni.inetAddresses)) {
                        val host = addr.hostAddress ?: continue
                        if (!addr.isLoopbackAddress && !host.contains(":")) result.add(host)
                    }
                }
            } catch (e: Exception) {
            }
            return result
        }
    }

    @Volatile private var running = false
    private var server: ServerSocket? = null
    @Volatile private var client: Socket? = null
    private val writeLock = Any()
    private val io = Executors.newSingleThreadExecutor()

    fun start() {
        if (running) return
        running = true
        Thread({ broadcastLoop() }, "tv-udp").start()
        Thread({ acceptLoop() }, "tv-tcp").start()
    }

    fun stop() {
        running = false
        try { server?.close() } catch (e: Exception) { }
        try { client?.close() } catch (e: Exception) { }
        io.shutdownNow()
    }

    fun sendHelloAsync() {
        try {
            io.execute { sendMsg(T_HELLO, helloInfo().toByteArray()) }
        } catch (e: Exception) {
        }
    }

    // ---------- UDP: anuncio ----------
    private fun broadcastTargets(): List<InetAddress> {
        val list = ArrayList<InetAddress>()
        try { list.add(InetAddress.getByName("255.255.255.255")) } catch (e: Exception) { }
        try {
            for (ni in Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp || ni.isLoopback) continue
                for (ia in ni.interfaceAddresses) {
                    val b = ia.broadcast
                    if (b != null) list.add(b)
                }
            }
        } catch (e: Exception) { }
        return list
    }

    private fun broadcastLoop() {
        val sock = DatagramSocket()
        sock.broadcast = true
        try {
            while (running) {
                val name = deviceName().replace("|", " ")
                val msg = "PROYECTATV1|$name|$TCP_PORT".toByteArray()
                for (addr in broadcastTargets()) {
                    try {
                        sock.send(DatagramPacket(msg, msg.size, addr, UDP_PORT))
                    } catch (e: Exception) {
                    }
                }
                Thread.sleep(2000)
            }
        } catch (e: Exception) {
        } finally {
            sock.close()
        }
    }

    // ---------- TCP: cuadros ----------
    private fun acceptLoop() {
        val srv = ServerSocket()
        server = srv
        try {
            srv.reuseAddress = true
            srv.bind(InetSocketAddress(TCP_PORT))
            while (running) {
                val s = try { srv.accept() } catch (e: Exception) { break }
                try { client?.close() } catch (e: Exception) { }
                client = s
                s.tcpNoDelay = true
                Thread({ readLoop(s) }, "tv-read").start()
            }
        } catch (e: Exception) {
        } finally {
            try { srv.close() } catch (e: Exception) { }
        }
    }

    private fun readLoop(s: Socket) {
        onConnected(true)
        sendHelloAsync()
        var buf = ByteArray(1 shl 20)
        try {
            val input = DataInputStream(BufferedInputStream(s.getInputStream(), 1 shl 16))
            while (running) {
                val type = input.readUnsignedByte()
                val len = input.readInt()
                if (len < 0 || len > 32 * 1024 * 1024) break
                if (len > buf.size) buf = ByteArray(len)
                input.readFully(buf, 0, len)
                when (type) {
                    T_FRAME -> {
                        onFrame(buf, len)
                        sendMsg(T_ACK, ByteArray(0))
                    }
                    T_ROT -> if (len >= 1) onRotation(buf[0].toInt())
                }
            }
        } catch (e: Exception) {
        } finally {
            try { s.close() } catch (e: Exception) { }
            if (client === s) {
                client = null
                onConnected(false)
            }
        }
    }

    private fun sendMsg(type: Int, payload: ByteArray) {
        val s = client ?: return
        try {
            synchronized(writeLock) {
                val out = s.getOutputStream()
                val header = ByteBuffer.allocate(5).put(type.toByte()).putInt(payload.size).array()
                out.write(header)
                if (payload.isNotEmpty()) out.write(payload)
                out.flush()
            }
        } catch (e: Exception) {
            try { s.close() } catch (e2: Exception) { }
        }
    }
}
