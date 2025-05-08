using System.Diagnostics;
using FileDownloadServer;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace FileDownloadClient;

public class Program
{
    private static readonly string _downloadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DownloadedFiles");
    private static readonly int _defaultSegmentSizeMB = 5; // Tamaño predeterminado en MB
    private static int _segmentSizeMB;
    
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Cliente de descarga de archivos segmentada mediante gRPC");
        Console.WriteLine("=======================================================\n");
        
        // Cargar configuración desde appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
            
        // Obtener el tamaño de segmento configurado o usar el valor predeterminado
        _segmentSizeMB = configuration.GetSection("DownloadSettings").GetValue<int>("SegmentSizeMB", _defaultSegmentSizeMB);
        Console.WriteLine($"Tamaño de segmento configurado: {_segmentSizeMB} MB\n");
        
        // Asegurar que exista el directorio de descarga
        if (!Directory.Exists(_downloadDirectory))
        {
            Directory.CreateDirectory(_downloadDirectory);
        }
        
        // Configurar el canal gRPC
        using var channel = GrpcChannel.ForAddress("http://localhost:5000");
        var client = new FileDownloader.FileDownloaderClient(channel);
        
        try
        {
            // Solicitar al usuario el nombre del archivo a descargar
            Console.Write("Ingrese el nombre del archivo a descargar (ej: sample.txt): ");
            string? fileName = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "sample.txt"; // Valor predeterminado
                Console.WriteLine($"Usando nombre de archivo predeterminado: {fileName}");
            }
            
            // Obtener información del archivo
            Console.WriteLine("\nObteniendo información del archivo...");
            var fileInfoResponse = await client.GetFileInfoAsync(new FileInfoRequest { FilePath = fileName });
            
            Console.WriteLine($"Archivo: {fileInfoResponse.FileName}");
            Console.WriteLine($"Tamaño: {FormatFileSize(fileInfoResponse.FileSize)}");
            Console.WriteLine($"Tipo de contenido: {fileInfoResponse.ContentType}");
            Console.WriteLine($"Segmentos recomendados: {fileInfoResponse.RecommendedSegments}");
            
            // Preguntar al usuario si desea usar el número recomendado de segmentos
            int segments = fileInfoResponse.RecommendedSegments;
            Console.Write($"\n¿Desea usar {segments} segmentos para la descarga? (s/n): ");
            string? response = Console.ReadLine()?.ToLower();
            
            if (response == "n")
            {
                Console.Write("Ingrese el número de segmentos a usar (1-16): ");
                if (int.TryParse(Console.ReadLine(), out int customSegments) && customSegments > 0 && customSegments <= 16)
                {
                    segments = customSegments;
                }
                else
                {
                    Console.WriteLine($"Valor no válido. Usando {segments} segmentos.");
                }
            }
            
            // Iniciar la descarga segmentada
            string outputFilePath = Path.Combine(_downloadDirectory, fileInfoResponse.FileName);
            var stopwatch = Stopwatch.StartNew();
            
            Console.WriteLine($"\nIniciando descarga con {segments} segmentos...");
            await DownloadFileInSegments(client, fileInfoResponse.FileId, segments, outputFilePath, fileInfoResponse.FileSize);
            
            stopwatch.Stop();
            Console.WriteLine($"\nDescarga completada en {stopwatch.ElapsedMilliseconds / 1000.0:F2} segundos");
            Console.WriteLine($"Velocidad promedio: {FormatFileSize(fileInfoResponse.FileSize / (stopwatch.ElapsedMilliseconds / 1000.0))}/s");
            Console.WriteLine($"Archivo guardado en: {outputFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Detalle: {ex.InnerException.Message}");
            }
        }
        
        Console.WriteLine("\nPresione cualquier tecla para salir...");
        Console.ReadKey();
    }
    
    private static async Task DownloadFileInSegments(FileDownloader.FileDownloaderClient client, string fileId, int totalSegments, string outputFilePath, long fileSize)
    {
        // Crear un array para almacenar las tareas de descarga
        var downloadTasks = new Task[totalSegments];
        var segmentFiles = new string[totalSegments];
        
        // Iniciar la descarga de cada segmento en paralelo
        for (int i = 0; i < totalSegments; i++)
        {
            int segmentNumber = i; // Capturar la variable para el lambda
            string segmentFilePath = $"{outputFilePath}.part{segmentNumber}";
            segmentFiles[segmentNumber] = segmentFilePath;
            
            downloadTasks[i] = Task.Run(async () =>
            {
                await DownloadSegment(client, fileId, segmentNumber, totalSegments, segmentFilePath);
            });
        }
        
        // Mostrar progreso mientras se descargan los segmentos
        var progressTask = Task.Run(async () =>
        {
            while (true)
            {
                // Verificar si todos los segmentos se han descargado
                if (downloadTasks.All(t => t.IsCompleted))
                    break;
                
                // Calcular el progreso basado en los archivos de segmentos
                long downloadedBytes = segmentFiles.Sum(f => File.Exists(f) ? new FileInfo(f).Length : 0);
                double progress = (double)downloadedBytes / fileSize * 100;
                
                Console.Write($"\rProgreso: {progress:F1}% ({FormatFileSize(downloadedBytes)} / {FormatFileSize(fileSize)})    ");
                
                await Task.Delay(1); // Actualizar cada 500ms
            }
        });
        
        // Esperar a que todos los segmentos se descarguen
        await Task.WhenAll(downloadTasks);
        await progressTask; // Asegurar que la tarea de progreso termine
        
        Console.WriteLine($"\rProgreso: 100% ({FormatFileSize(fileSize)} / {FormatFileSize(fileSize)})    ");
        Console.WriteLine("Combinando segmentos...");
        
        // Combinar todos los segmentos en el archivo final
        using (var outputStream = new FileStream(outputFilePath, FileMode.Create))
        {
            for (int i = 0; i < totalSegments; i++)
            {
                string segmentFilePath = segmentFiles[i];
                using (var segmentStream = new FileStream(segmentFilePath, FileMode.Open))
                {
                    await segmentStream.CopyToAsync(outputStream);
                }
                
                // Eliminar el archivo de segmento después de combinarlo
                File.Delete(segmentFilePath);
            }
        }
    }
    
    private static async Task DownloadSegment(FileDownloader.FileDownloaderClient client, string fileId, int segmentNumber, int totalSegments, string outputFilePath)
    {
        // Crear la solicitud para el segmento
        var request = new SegmentRequest
        {
            FileId = fileId,
            SegmentNumber = segmentNumber,
            TotalSegments = totalSegments,
            SegmentSizeMb = _segmentSizeMB // Agregar el tamaño de segmento configurado
        };
        
        // Obtener el stream de respuesta
        using var call = client.DownloadSegment(request);
        using var outputFile = new FileStream(outputFilePath, FileMode.Create);
        
        // Leer y escribir los datos del segmento
        // Reemplazo de ReadAllAsync por bucle while
        while (await call.ResponseStream.MoveNext(System.Threading.CancellationToken.None))
        {
            var response = call.ResponseStream.Current;
            // Procesar la respuesta aquí
            await outputFile.WriteAsync(response.Data.Memory);
        }
    }
    
    private static string FormatFileSize(double bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int suffixIndex = 0;
        
        while (bytes >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            bytes /= 1024;
            suffixIndex++;
        }
        
        return $"{bytes:F2} {suffixes[suffixIndex]}";
    }
}