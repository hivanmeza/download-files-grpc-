using System.Collections.Concurrent;
using Grpc.Core;

namespace FileDownloadServer.Services;

public class FileDownloaderService : FileDownloader.FileDownloaderBase
{
    private readonly ILogger<FileDownloaderService> _logger;
    private readonly string _downloadDirectory;
    private static readonly ConcurrentDictionary<string, string> _fileRegistry = new();

    public FileDownloaderService(ILogger<FileDownloaderService> logger)
    {
        _logger = logger;
        _downloadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DownloadFiles");
    }

    public override Task<FileInfoResponse> GetFileInfo(FileInfoRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Recibida solicitud de información para archivo: {FilePath}", request.FilePath);

        // Validar y normalizar la ruta del archivo
        string normalizedPath = request.FilePath.Replace("/", "\\");
        string fullPath = Path.GetFullPath(Path.Combine(_downloadDirectory, normalizedPath));

        // Verificar que el archivo esté dentro del directorio permitido
        if (!fullPath.StartsWith(_downloadDirectory))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Ruta de archivo no válida"));
        }

        // Verificar que el archivo exista
        if (!File.Exists(fullPath))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Archivo no encontrado"));
        }

        // Obtener información del archivo
        var fileInfo = new FileInfo(fullPath);
        string fileId = Guid.NewGuid().ToString();
        _fileRegistry[fileId] = fullPath;

        // Calcular número recomendado de segmentos basado en el tamaño del archivo
        int recommendedSegments = CalculateRecommendedSegments(fileInfo.Length);

        return Task.FromResult(new FileInfoResponse
        {
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            ContentType = GetContentType(fileInfo.Extension),
            RecommendedSegments = recommendedSegments,
            FileId = fileId
        });
    }

    public override async Task DownloadSegment(SegmentRequest request, IServerStreamWriter<SegmentResponse> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Recibida solicitud de segmento {SegmentNumber}/{TotalSegments} para archivo: {FileId}", 
            request.SegmentNumber, request.TotalSegments, request.FileId);

        // Verificar que el ID del archivo sea válido
        if (!_fileRegistry.TryGetValue(request.FileId, out string? filePath))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "ID de archivo no válido"));
        }

        // Verificar que el archivo exista
        if (!File.Exists(filePath))
        {
            _fileRegistry.TryRemove(request.FileId, out _);
            throw new RpcException(new Status(StatusCode.NotFound, "Archivo no encontrado"));
        }

        // Obtener información del archivo
        var fileInfo = new FileInfo(filePath);
        long fileSize = fileInfo.Length;

        // Calcular el tamaño y offset del segmento
        int totalSegments = request.TotalSegments;
        int segmentNumber = request.SegmentNumber;

        if (segmentNumber < 0 || segmentNumber >= totalSegments)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Número de segmento no válido"));
        }

        // Usar el tamaño de segmento personalizado si está especificado
        long segmentSize;
        long remainder;
        long offset;
        
        if (request.SegmentSizeMb > 0)
        {
            // Convertir MB a bytes
            long requestedSegmentSize = request.SegmentSizeMb * 1024 * 1024;
            _logger.LogInformation("Usando tamaño de segmento personalizado: {SegmentSizeMb} MB", request.SegmentSizeMb);
            
            // Calcular cuántos segmentos completos del tamaño solicitado caben en el archivo
            long maxSegments = (fileSize + requestedSegmentSize - 1) / requestedSegmentSize;
            
            // Si el número de segmentos solicitado es mayor que el máximo posible, ajustar el tamaño
            if (totalSegments > maxSegments)
            {
                segmentSize = fileSize / totalSegments;
                remainder = fileSize % totalSegments;
                offset = segmentNumber * segmentSize;
                
                // Ajustar el tamaño del último segmento para incluir el resto
                if (segmentNumber == totalSegments - 1)
                {
                    segmentSize += remainder;
                }
            }
            else
            {
                // Usar el tamaño solicitado
                segmentSize = requestedSegmentSize;
                remainder = fileSize % requestedSegmentSize;
                offset = segmentNumber * requestedSegmentSize;
                
                // Ajustar el tamaño del último segmento
                if (segmentNumber == totalSegments - 1)
                {
                    // El último segmento puede ser más pequeño
                    long lastSegmentSize = fileSize - (offset);
                    segmentSize = lastSegmentSize > 0 ? lastSegmentSize : segmentSize;
                }
                
                // Asegurarse de que el offset no exceda el tamaño del archivo
                if (offset >= fileSize)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Offset de segmento fuera de rango"));
                }
            }
        }
        else
        {
            // Usar el método original basado en el número de segmentos
            segmentSize = fileSize / totalSegments;
            remainder = fileSize % totalSegments;
            offset = segmentNumber * segmentSize;
            
            // Ajustar el tamaño del último segmento para incluir el resto
            if (segmentNumber == totalSegments - 1)
            {
                segmentSize += remainder;
            }
        }

        // Leer y enviar el segmento
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fileStream.Seek(offset, SeekOrigin.Begin);

        // Tamaño del buffer para enviar datos en trozos más pequeños
        const int bufferSize = 64 * 1024; // 64 KB
        var buffer = new byte[Math.Min(bufferSize, segmentSize)];
        long bytesRemaining = segmentSize;

        while (bytesRemaining > 0 && !context.CancellationToken.IsCancellationRequested)
        {
            int bytesToRead = (int)Math.Min(buffer.Length, bytesRemaining);
            int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead), context.CancellationToken);

            if (bytesRead == 0)
                break; // Fin del archivo

            bytesRemaining -= bytesRead;

            // Enviar el trozo de datos
            await responseStream.WriteAsync(new SegmentResponse
            {
                Data = Google.Protobuf.ByteString.CopyFrom(buffer, 0, bytesRead),
                SegmentNumber = segmentNumber,
                TotalSegments = totalSegments,
                Offset = offset + (segmentSize - bytesRemaining - bytesRead),
                SegmentSize = bytesRead
            });
        }

        _logger.LogInformation("Segmento {SegmentNumber}/{TotalSegments} enviado correctamente", 
            segmentNumber, totalSegments);
    }

    private static int CalculateRecommendedSegments(long fileSize)
    {
        // Lógica para determinar el número óptimo de segmentos basado en el tamaño del archivo
        if (fileSize < 1024 * 1024) // < 1 MB
            return 1;
        if (fileSize < 10 * 1024 * 1024) // < 10 MB
            return 2;
        if (fileSize < 50 * 1024 * 1024) // < 50 MB
            return 4;
        if (fileSize < 100 * 1024 * 1024) // < 100 MB
            return 8;
        
        return 16; // Para archivos grandes
    }

    private static string GetContentType(string fileExtension)
    {
        return fileExtension.ToLower() switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".doc" or ".docx" => "application/msword",
            ".xls" or ".xlsx" => "application/vnd.ms-excel",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };
    }
}