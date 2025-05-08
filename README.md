# API gRPC para Descarga Segmentada de Archivos

Este proyecto implementa una API gRPC con .NET 9 que permite la descarga de archivos de manera segmentada para optimizar la velocidad de transferencia. La solución incluye un servidor gRPC que divide los archivos en segmentos y un cliente de consola que permite descargar estos segmentos en paralelo y reconstruir el archivo original.

## Estructura del Proyecto

- **FileDownloadServer**: Servidor gRPC que implementa la API para descarga segmentada de archivos.
- **FileDownloadClient**: Cliente de consola que consume la API y permite probar la funcionalidad.

## Características

- Descarga de archivos dividida en múltiples segmentos
- Descarga paralela de segmentos para maximizar la velocidad
- Cálculo automático del número óptimo de segmentos según el tamaño del archivo
- Reconstrucción automática del archivo original a partir de los segmentos
- Monitoreo de progreso en tiempo real
- Cálculo de velocidad de descarga

## Requisitos

- .NET 9 SDK
- Visual Studio 2022 o superior (opcional)

## Cómo Ejecutar

### Servidor

1. Navegar al directorio del servidor:
   ```
   cd FileDownloadServer
   ```

2. Ejecutar el servidor:
   ```
   dotnet run
   ```

   El servidor se iniciará en `http://localhost:5000` y creará automáticamente un archivo de ejemplo para pruebas.

### Cliente

1. En otra terminal, navegar al directorio del cliente:
   ```
   cd FileDownloadClient
   ```

2. Ejecutar el cliente:
   ```
   dotnet run
   ```

3. Seguir las instrucciones en pantalla para descargar un archivo.

## Cómo Funciona

### Proceso de Descarga Segmentada

1. El cliente solicita información del archivo al servidor mediante `GetFileInfo`.
2. El servidor responde con metadatos del archivo, incluyendo tamaño y número recomendado de segmentos.
3. El cliente inicia múltiples solicitudes paralelas mediante `DownloadSegment`, una para cada segmento del archivo.
4. El servidor divide el archivo y transmite cada segmento al cliente.
5. El cliente guarda cada segmento como un archivo temporal y muestra el progreso general.
6. Una vez completada la descarga de todos los segmentos, el cliente los combina en un único archivo final.

## Personalización

Para usar la API con sus propios archivos, coloque los archivos en el directorio `DownloadFiles` dentro del directorio del servidor.

## Rendimiento

La descarga segmentada puede mejorar significativamente la velocidad de transferencia, especialmente para archivos grandes y en conexiones con alta latencia. El número óptimo de segmentos varía según el tamaño del archivo y las características de la red.

## Extensiones Posibles

- Implementar autenticación y autorización
- Añadir compresión de datos
- Implementar reanudación de descargas interrumpidas
- Crear una interfaz gráfica para el cliente
- Añadir cifrado de extremo a extremo

## Recomendaciones para la Implementación

### Seguridad

- **Autenticación**: Implementar autenticación mediante JWT o certificados SSL/TLS para asegurar que solo usuarios autorizados puedan acceder al servicio.
- **Autorización**: Definir roles y permisos para controlar qué archivos puede descargar cada usuario.
- **Validación de Entradas**: Validar todas las entradas del cliente para prevenir ataques de inyección o acceso a rutas no autorizadas.
- **Limitación de Tasa**: Implementar límites de solicitudes por usuario para prevenir ataques de denegación de servicio.
- **Cifrado**: Utilizar TLS para cifrar todas las comunicaciones entre cliente y servidor.
- **Auditoría**: Registrar todas las operaciones de descarga para fines de seguimiento y cumplimiento.

### Rendimiento

- **Tamaño de Segmento Óptimo**: Ajustar el tamaño de segmento según las características de la red. Generalmente, segmentos de 1-5 MB funcionan bien en la mayoría de las redes.
- **Número de Segmentos**: Limitar el número máximo de segmentos paralelos a 8-10 para evitar saturar la conexión.
- **Compresión**: Implementar compresión para archivos de texto o datos comprimibles para reducir el tiempo de transferencia.
- **Caché**: Utilizar caché para metadatos de archivos frecuentemente solicitados.
- **Monitoreo**: Implementar métricas para supervisar el rendimiento y detectar cuellos de botella.

### Escalabilidad

- **Balanceo de Carga**: Distribuir las solicitudes entre múltiples instancias del servidor.
- **Almacenamiento Distribuido**: Considerar sistemas de almacenamiento distribuido para archivos grandes.
- **Microservicios**: Separar la funcionalidad de descarga de archivos en un microservicio independiente si forma parte de una aplicación más grande.

## Implementación para .NET Framework 4.8

.NET Framework 4.8 no tiene soporte nativo para gRPC, pero es posible consumir la API con algunas modificaciones. A continuación se detallan los pasos necesarios:

### Configuración del Cliente

1. **Dependencias Necesarias**: Instalar los siguientes paquetes NuGet en tu proyecto .NET Framework 4.8:
   ```
   Install-Package Grpc.Core
   Install-Package Google.Protobuf
   Install-Package Grpc.Tools
   ```

2. **Generación de Código Cliente**: Copiar el archivo `filedownload.proto` a tu proyecto y configurar la generación de código en el archivo `.csproj`:
   ```xml
   <ItemGroup>
     <PackageReference Include="Grpc.Tools" Version="2.51.0" PrivateAssets="All" />
   </ItemGroup>
   <ItemGroup>
     <Protobuf Include="Protos\filedownload.proto" GrpcServices="Client" />
   </ItemGroup>
   ```

### Modificaciones Necesarias

1. **Manejo de Streams**: .NET Framework 4.8 tiene limitaciones con streams asíncronos. Implementar un wrapper que convierta los streams gRPC a un formato compatible:

   ```csharp
   public class FileDownloaderClient
   {
       private readonly FileDownloader.FileDownloaderClient _client;
       
       public FileDownloaderClient(string serverAddress)
       {
           var channel = new Channel(serverAddress, ChannelCredentials.Insecure);
           _client = new FileDownloader.FileDownloaderClient(channel);
       }
       
       public async Task<FileInfoResponse> GetFileInfoAsync(string filePath)
       {
           var request = new FileInfoRequest { FilePath = filePath };
           return await Task.Run(() => _client.GetFileInfo(request));
       }
       
       public List<byte[]> DownloadSegments(string fileId, int totalSegments, int segmentSizeMb, 
           IProgress<(int segmentNumber, int totalSegments, long bytesReceived)> progress)
       {
           var segments = new List<byte[]>(totalSegments);
           for (int i = 0; i < totalSegments; i++)
           {
               segments.Add(null); // Inicializar lista con espacios para todos los segmentos
           }
           
           var tasks = new List<Task>();
           for (int i = 0; i < totalSegments; i++)
           {
               var segmentNumber = i;
               var task = Task.Run(() =>
               {
                   var request = new SegmentRequest
                   {
                       FileId = fileId,
                       SegmentNumber = segmentNumber,
                       TotalSegments = totalSegments,
                       SegmentSizeMb = segmentSizeMb
                   };
                   
                   using (var call = _client.DownloadSegment(request))
                   {
                       var responseStream = call.ResponseStream;
                       var segmentData = new MemoryStream();
                       
                       while (responseStream.MoveNext().Result)
                       {
                           var response = responseStream.Current;
                           response.Data.WriteTo(segmentData);
                           progress?.Report((segmentNumber, totalSegments, segmentData.Length));
                       }
                       
                       segments[segmentNumber] = segmentData.ToArray();
                   }
               });
               
               tasks.Add(task);
           }
           
           Task.WaitAll(tasks.ToArray());
           return segments;
       }
       
       public void SaveToFile(List<byte[]> segments, string outputPath)
       {
           using (var fileStream = new FileStream(outputPath, FileMode.Create))
           {
               foreach (var segment in segments)
               {
                   fileStream.Write(segment, 0, segment.Length);
               }
           }
       }
   }
   ```

2. **Manejo de Excepciones**: Implementar un manejo robusto de excepciones para lidiar con problemas de conectividad:

   ```csharp
   try
   {
       // Código de descarga
   }
   catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
   {
       Console.WriteLine("El servidor no está disponible. Verifique la conexión.");
   }
   catch (RpcException ex)
   {
       Console.WriteLine($"Error gRPC: {ex.Status.Detail}");
   }
   catch (Exception ex)
   {
       Console.WriteLine($"Error: {ex.Message}");
   }
   ```

### Ejemplo de Uso

```csharp
var client = new FileDownloaderClient("localhost:5000");

// Obtener información del archivo
var fileInfo = client.GetFileInfoAsync("ejemplo.mp4").Result;
Console.WriteLine($"Archivo: {fileInfo.FileName}, Tamaño: {fileInfo.FileSize} bytes");

// Configurar progreso
var progress = new Progress<(int segmentNumber, int totalSegments, long bytesReceived)>(p =>
{
    var percentage = (int)((p.segmentNumber + 1) * 100.0 / p.totalSegments);
    Console.WriteLine($"Progreso: {percentage}% - Segmento {p.segmentNumber + 1}/{p.totalSegments}");
});

// Descargar segmentos
var segments = client.DownloadSegments(
    fileInfo.FileId,
    fileInfo.RecommendedSegments,
    4, // 4MB por segmento
    progress);

// Guardar archivo completo
client.SaveToFile(segments, $"C:\\Downloads\\{fileInfo.FileName}");
Console.WriteLine("Descarga completada.");
```

### Consideraciones Adicionales

- **Rendimiento**: .NET Framework 4.8 puede tener un rendimiento inferior en comparación con .NET 9 para operaciones asíncronas y de streaming.
- **Compatibilidad**: Asegurarse de que las versiones de los paquetes gRPC sean compatibles entre sí.
- **Memoria**: Monitorear el uso de memoria, especialmente al descargar archivos grandes, ya que .NET Framework 4.8 tiene limitaciones en la gestión de memoria comparado con .NET 9.
- **Pruebas**: Realizar pruebas exhaustivas en diferentes escenarios de red para garantizar la estabilidad.