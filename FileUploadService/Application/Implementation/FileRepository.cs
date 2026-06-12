using Dapper;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using Npgsql;

namespace FileUploadService.Application.Implementation;

/// <summary>
/// All PostgreSQL operations for file metadata.
/// FileService never writes SQL — it calls IFileRepository.
/// </summary>
public class FileRepository : IFileRepository
{
    private readonly string _connectionString;
    private readonly ILogger<FileRepository> _logger;

    // Injected in tests to allow SQLite or other in-memory DBs
    private readonly System.Data.IDbConnection? _testConnection;

    public FileRepository(IConfiguration configuration, ILogger<FileRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    // Test-only constructor
    internal FileRepository(
        IConfiguration configuration,
        ILogger<FileRepository> logger,
        System.Data.IDbConnection testConnection)
        : this(configuration, logger)
    {
        _testConnection = testConnection;
    }

    public async Task SaveAsync(FileMetadata metadata, CancellationToken ct = default)
    {
        const string sql = @"
        INSERT INTO files (
            reference_id, original_filename, storage_path,
            content_type, file_size, uploaded_by, created_at, is_deleted
        ) VALUES (
            @referenceId, @originalFilename, @storagePath,
            @contentType, @fileSize, @uploadedBy, @createdAt, @isDeleted
        )
        RETURNING id";
    
    await using var conn = new NpgsqlConnection(_connectionString);

        // Change ExecuteAsync → ExecuteScalarAsync so we get the id back
        metadata.Id = await conn.ExecuteScalarAsync<long>(sql, new
        {
            referenceId = metadata.ReferenceId,
            originalFilename = metadata.OriginalFilename,
            storagePath = metadata.StoragePath,
            contentType = metadata.ContentType,
            fileSize = metadata.FileSize,
            uploadedBy = metadata.UploadedBy,
            createdAt = metadata.CreatedAt,
            isDeleted = metadata.IsDeleted
        });
    }

    public async Task<FileMetadata?> GetByReferenceIdAsync(string referenceId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                id                AS Id,
                reference_id      AS ReferenceId,
                original_filename AS OriginalFilename,
                storage_path      AS StoragePath,
                content_type      AS ContentType,
                file_size         AS FileSize,
                uploaded_by       AS UploadedBy,
                created_at        AS CreatedAt,
                is_deleted        AS IsDeleted
            FROM files
            WHERE reference_id = @referenceId
              AND is_deleted   = FALSE";

        await using var conn = GetConnection();
        return await conn.QuerySingleOrDefaultAsync<FileMetadata>(sql, new { referenceId });
    }

    public async Task<bool> SoftDeleteAsync(string referenceId, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE files
            SET    is_deleted = TRUE
            WHERE  reference_id = @referenceId
              AND  is_deleted   = FALSE";

        await using var conn = GetConnection();
        var affected = await conn.ExecuteAsync(sql, new { referenceId });
        return affected > 0;
    }

    // Returns the test connection unwrapped, or a new NpgsqlConnection
    private NpgsqlConnection GetConnection() =>
        _testConnection is NpgsqlConnection pg
            ? pg
            : new NpgsqlConnection(_connectionString);
}