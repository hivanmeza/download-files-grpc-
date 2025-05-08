# Cliente de Descarga de Archivos Segmentada

## Configuración

El cliente permite configurar el tamaño de los segmentos de descarga a través del archivo `appsettings.json`. La configuración predeterminada es:

```json
{
  "DownloadSettings": {
    "SegmentSizeMB": 5
  }
}
```

Donde:
- `SegmentSizeMB`: Define el tamaño de cada segmento en megabytes (MB).

## Uso

Al ejecutar el cliente, automáticamente leerá la configuración del archivo `appsettings.json`. Si el archivo no existe o no contiene la configuración, se utilizará el valor predeterminado de 5 MB por segmento.

Puede modificar este valor según sus necesidades y el rendimiento de su red. Un valor más alto puede mejorar el rendimiento en conexiones rápidas, mientras que un valor más bajo puede ser más adecuado para conexiones lentas o inestables.

## Ejecución

Para ejecutar el cliente:

```
dotnet run
```

Siga las instrucciones en pantalla para descargar un archivo.