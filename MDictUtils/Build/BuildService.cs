using MDictUtils.Build.Blocks;
using MDictUtils.Build.Compression;
using MDictUtils.Build.Index;
using MDictUtils.Build.Offset;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build;

internal static class MDictDataBuilderProvider
{
    public static IMDictDataBuilder GetDataBuilder(MDictMetadata metadata, bool logging)
    {
        var s = new ServiceCollection();

        // Offset table
        s.AddTransient<OffsetTableBuilder>();
        if (metadata.IsMdd)
            s.AddTransient<IKeyComparer, MddKeyComparer>();
        else
            s.AddTransient<IKeyComparer, MdxKeyComparer>();

        // Key blocks
        s.AddTransient<KeyBlockIndexBuilder>();
        s.AddTransient<KeyBlocksBuilder>();

        // Record blocks
        s.AddTransient<RecordBlockIndexBuilder>();
        if (metadata.IsMdd)
            s.AddTransient<IRecordBlocksBuilder, MddRecordBlocksBuilder>();
        else
            s.AddTransient<IRecordBlocksBuilder, MdxRecordBlocksBuilder>();

        // Compression
        if (metadata.CompressionType == ZLibBlockCompressor.CompressionType)
            s.AddTransient<IBlockCompressor, ZLibBlockCompressor>();
        else
            throw new NotSupportedException($"Unsupported compression type `{metadata.CompressionType}`");

        // Logging
        s.AddLogging(builder =>
        {
            if (logging) builder.SetMinimumLevel(LogLevel.Debug);

            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = false;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });

        // Build and return the builder service.
        s.AddTransient<IMDictDataBuilder, MDictDataBuilder>();

        var provider = s.BuildServiceProvider();
        return provider.GetRequiredService<IMDictDataBuilder>();
    }
}
