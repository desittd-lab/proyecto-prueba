# ProyectaTV

Proyecta el escritorio de **Windows 11** en un **Android TV** por la red Wi-Fi, con vista
horizontal o vertical (TV de pie) y zoom que sigue al mouse.

```
ProyectaTV/
├─ .github/workflows/build.yml   ← compila todo en GitHub Actions
├─ windows/ProyectaTV/           ← programa de Windows (C# .NET 8, WPF)
└─ android-tv/                   ← app receptora para Android TV (Kotlin)
```

## 1. Compilar en GitHub

1. Crea un repositorio nuevo en GitHub.
2. Descomprime este .zip y sube **todo el contenido** a la raíz del repositorio
   (la carpeta `.github` debe quedar en la raíz).
3. Entra a la pestaña **Actions**. La compilación arranca sola con cada `push`
   (también puedes lanzarla con *Run workflow*).
4. Al terminar, abre la ejecución y descarga los *Artifacts*:
   - `ProyectaTV-Windows` → `ProyectaTV.exe` (autónomo, no requiere instalar .NET)
   - `ProyectaTV-AndroidTV` → `ProyectaTV-Receptor.apk`

Para publicar una *release* con ambos archivos: crea una etiqueta, por ejemplo `v1.0.0`.

## 2. Instalar

- **Windows 11:** ejecuta `ProyectaTV.exe`. La primera vez Windows pedirá permiso de red:
  marca **Redes privadas** y permite el acceso (necesario para encontrar el TV).
- **Android TV:** instala el APK (activa *Orígenes desconocidos* para la app que uses).
  Opciones: app *Downloader*, memoria USB, o `adb install ProyectaTV-Receptor.apk`.

## 3. Usar

1. PC y TV en la **misma red Wi-Fi**.
2. Abre **ProyectaTV Receptor** en el TV: muestra su nombre e IP.
3. En Windows, elige el TV de la lista (o escribe la IP) y pulsa **Conectar**.
4. Elige la vista:
   - **Horizontal:** escritorio completo, a escala, centrado.
   - **Vertical:** activa *Pantalla completa con zoom enfocado en el mouse* y ajusta el zoom
     (1× a 4×). La imagen llena el TV y sigue al cursor. Si lo desactivas, se muestra el
     escritorio completo ajustado al ancho.
5. Pulsa **Proyectar escritorio**.

### El TV está de pie: giro de imagen
Android TV no gira la pantalla por sí solo, así que la app del TV gira la imagen.
Por defecto gira 90° horario. Si se ve de lado o al revés, cambia el **Giro** en Windows
o pulsa **OK / MENÚ** en el control remoto del TV. El valor se guarda.

## Detalles técnicos
- Descubrimiento: el TV anuncia por UDP `50505`; la conexión de video es TCP `50506`.
- Windows captura el monitor principal, compone el cuadro con el tamaño exacto del TV,
  lo comprime en JPEG y lo envía. El TV confirma cada cuadro (control de flujo para
  evitar retraso).
- Calidad y FPS se ajustan en la app: baja (1280 px), media (1600 px), alta (1920 px).

## Problemas frecuentes
- **El TV no aparece en la lista:** permite ProyectaTV en el Firewall de Windows (red privada)
  y escribe la IP del TV a mano. Algunos routers aíslan los dispositivos Wi-Fi
  (*AP isolation*): desactívalo.
- **Se ve lento:** baja la calidad o los FPS. Lo ideal es Wi-Fi 5 GHz o TV por cable.
- **Solo se proyecta el monitor principal.**
