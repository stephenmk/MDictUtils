using MDictUtils.Build;
using MDictUtils.Build.Blocks;
using MDictUtils.Build.Compression;
using MDictUtils.Build.Index;
using MDictUtils.Build.Offset;
using MDictUtils.BuildModels;
using MDictUtils.Write;
using Microsoft.Extensions.DependencyInjection;
using static MDictUtils.MDictKeyEncodingType;

namespace MDictUtils;

public interface IMdxWriter
{
    Task WriteAsync(MdxHeader header, List<MDictEntry> entries, string outputFile);
}

public interface IMddWriter
{
    Task WriteAsync(MddHeader header, List<MDictEntry> entries, string outputFile);
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMdxWriter(this IServiceCollection services, Action<MdxWriterOptions>? configure = null)
    {
        var options = new MdxWriterOptions();
        if (configure is not null)
            configure(options);

        return services
            .AddLogging()
            .AddMdxWriterServices()
            .AddMdxBuilderServices(options);
    }

    public static IServiceCollection AddMddWriter(this IServiceCollection services, Action<MddWriterOptions>? configure = null)
    {
        var options = new MddWriterOptions();
        if (configure is not null)
            configure(options);

        return services
            .AddLogging()
            .AddMddWriterServices()
            .AddMddBuilderServices(options);
    }

    private static IServiceCollection AddMdxWriterServices(this IServiceCollection services)
        => services
            .AddTransient<IMdxWriter, Writer>()
            .AddTransient<HeaderWriter>()
            .AddTransient<KeysWriter>()
            .AddTransient<RecordsWriter>();

    private static IServiceCollection AddMddWriterServices(this IServiceCollection services)
        => services
            .AddTransient<IMddWriter, Writer>()
            .AddTransient<HeaderWriter>()
            .AddTransient<KeysWriter>()
            .AddTransient<RecordsWriter>();

    private static IServiceCollection AddMdxBuilderServices(this IServiceCollection services, MdxWriterOptions options)
        => services
            .AddTransient<IDataBuilder, DataBuilder>()
            .AddTransient<IKeyComparer, MdxKeyComparer>()
            .AddTransient<OffsetTableBuilder>()
            .AddTransient<KeyBlockIndexBuilder>()
            .AddTransient<KeyBlocksBuilder>()
            .AddTransient<IRecordBlocksBuilder, MdxRecordBlocksBuilder>()
            .AddMdxBuildOptions(options)
            .AddBlockCompressor(options.CompressionType);

    private static IServiceCollection AddMddBuilderServices(this IServiceCollection services, MddWriterOptions options)
        => services
            .AddTransient<IDataBuilder, DataBuilder>()
            .AddTransient<IKeyComparer, MddKeyComparer>()
            .AddTransient<OffsetTableBuilder>()
            .AddTransient<KeyBlockIndexBuilder>()
            .AddTransient<KeyBlocksBuilder>()
            .AddTransient<IRecordBlocksBuilder, MddRecordBlocksBuilder>()
            .AddMddBuildOptions(options)
            .AddBlockCompressor(options.CompressionType);

    private static IServiceCollection AddMdxBuildOptions(this IServiceCollection services, MdxWriterOptions options)
        => services.AddTransient(_ => new BuildOptions
        {
            DesiredKeyBlockSize = options.DesiredKeyBlockSize,
            DesiredRecordBlockSize = options.DesiredRecordBlockSize,
            KeyEncoding = options.KeyEncoding.ToEncoding(),
            KeyEncodingLength = options.KeyEncoding.ToEncodingLength(),
        });

    private static IServiceCollection AddMddBuildOptions(this IServiceCollection services, MddWriterOptions options)
        => services.AddTransient(_ => new BuildOptions
        {
            DesiredKeyBlockSize = options.DesiredKeyBlockSize,
            DesiredRecordBlockSize = options.DesiredRecordBlockSize,
            KeyEncoding = Utf16.ToEncoding(),
            KeyEncodingLength = Utf16.ToEncodingLength(),
        });

    private static IServiceCollection AddBlockCompressor(this IServiceCollection services, MDictCompressionType compressionType)
        => compressionType switch
        {
            MDictCompressionType.ZLib
                => services.AddTransient<IBlockCompressor, ZLibBlockCompressor>(),
            _ // Default
                => throw new NotSupportedException($"Unsupported compression type `{compressionType}`")
        };
}
