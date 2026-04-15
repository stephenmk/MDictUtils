using Lib.BuildModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lib.Build;

internal interface IMDictDataBuilder
{
    public MDictData BuildData(List<MDictEntry> entries, MDictMetadata metadata);
}

internal interface IBlockCompressor
{
    ImmutableArray<byte> Compress(ReadOnlySpan<byte> data);
}

internal static class MDictDataBuilderProvider
{
    public static IMDictDataBuilder GetDataBuilder(MDictMetadata metadata, bool logging)
    {
        var s = new ServiceCollection();

        // Offset table
        s.AddTransient<OffsetTableBuilder>();
        s.AddTransient<MDictKeyComparer>();

        // Key blocks
        s.AddTransient<KeyBlockIndexBuilder>();
        s.AddTransient<KeyBlocksBuilder>();

        // Record blocks
        s.AddTransient<RecordBlockIndexBuilder>();
        s.AddTransient<RecordBlocksBuilder>();

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
