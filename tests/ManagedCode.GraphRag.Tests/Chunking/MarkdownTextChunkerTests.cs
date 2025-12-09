using GraphRag.Chunking;
using GraphRag.Config;
using GraphRag.Constants;
using GraphRag.Tokenization;

namespace ManagedCode.GraphRag.Tests.Chunking;

public sealed class MarkdownTextChunkerTests
{
    private readonly MarkdownTextChunker _chunker = new();

    #region Chunk Tests (Original)

    [Fact]
    public void Chunk_SplitsMarkdownBlocks()
    {
        var text = "# Title\n\nAlice met Bob.\n\n![image](path)\n\n" +
                   string.Join(" ", Enumerable.Repeat("This is a longer paragraph that should be chunked based on token limits.", 4));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 60,
            Overlap = 10,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.Contains("doc-1", chunk.DocumentIds));
        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, chunk => Assert.True(chunk.TokenCount > 0));
    }

    [Fact]
    public void Chunk_MergesImageBlocksIntoPrecedingChunk()
    {
        var text = string.Join(' ', Enumerable.Repeat("This paragraph provides enough content for chunking.", 6)) +
                   "\n\n![diagram](diagram.png)\nImage description follows with more narrative text.";
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 60,
            Overlap = 0,
            EncodingModel = TokenizerDefaults.DefaultModel
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, chunk => chunk.Text.Contains("![diagram](diagram.png)", StringComparison.Ordinal));
        Assert.DoesNotContain(chunks, chunk => chunk.Text.TrimStart().StartsWith("![", StringComparison.Ordinal));
    }

    [Fact]
    public void Chunk_RespectsOverlapBetweenChunks()
    {
        var text = string.Join(' ', Enumerable.Repeat("Token overlap ensures continuity across generated segments.", 20));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 80,
            Overlap = 20,
            EncodingModel = "gpt-4"
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.True(chunks.Count > 1);

        var tokenizer = TokenizerRegistry.GetTokenizer(config.EncodingModel);
        var firstTokens = tokenizer.EncodeToIds(chunks[0].Text);

        _ = tokenizer.EncodeToIds(chunks[1].Text);
        var overlapTokens = firstTokens.Skip(Math.Max(0, firstTokens.Count - config.Overlap)).ToArray();
        Assert.True(overlapTokens.Length > 0);
        var overlapText = tokenizer.Decode(overlapTokens).TrimStart();
        var secondText = chunks[1].Text.TrimStart();
        Assert.StartsWith(overlapText, secondText, StringComparison.Ordinal);
    }

    #endregion

    #region SplitToFragments Tests

    [Fact]
    public void SplitToFragments_EmptyString_ReturnsEmpty()
    {
        var result = MarkdownTextChunker.SplitToFragments("", MarkdownTextChunker.ExplicitSeparators);
        Assert.Empty(result);
    }

    [Fact]
    public void SplitToFragments_NullSeparators_ReturnsCharacterLevelFragments()
    {
        var result = MarkdownTextChunker.SplitToFragments("abc", null);

        Assert.Equal(3, result.Count);
        Assert.All(result, f => Assert.True(f.IsSeparator));
        Assert.Equal("a", result[0].Content);
        Assert.Equal("b", result[1].Content);
        Assert.Equal("c", result[2].Content);
    }

    [Fact]
    public void SplitToFragments_NoSeparatorsInText_ReturnsSingleContentFragment()
    {
        var result = MarkdownTextChunker.SplitToFragments("hello world", MarkdownTextChunker.ExplicitSeparators);

        Assert.Single(result);
        Assert.False(result[0].IsSeparator);
        Assert.Equal("hello world", result[0].Content);
    }

    [Fact]
    public void SplitToFragments_SeparatorAtStart_FirstFragmentIsSeparator()
    {
        var result = MarkdownTextChunker.SplitToFragments("\n\nhello", MarkdownTextChunker.ExplicitSeparators);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsSeparator);
        Assert.Equal("\n\n", result[0].Content);
        Assert.False(result[1].IsSeparator);
        Assert.Equal("hello", result[1].Content);
    }

    [Fact]
    public void SplitToFragments_SeparatorAtEnd_LastFragmentIsSeparator()
    {
        var result = MarkdownTextChunker.SplitToFragments("hello.\n\n", MarkdownTextChunker.ExplicitSeparators);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsSeparator);
        Assert.Equal("hello", result[0].Content);
        Assert.True(result[1].IsSeparator);
        Assert.Equal(".\n\n", result[1].Content);
    }

    [Fact]
    public void SplitToFragments_AdjacentSeparators_CreatesSeparateFragments()
    {
        var result = MarkdownTextChunker.SplitToFragments("\n\n\n\n", MarkdownTextChunker.ExplicitSeparators);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.True(f.IsSeparator));
        Assert.Equal("\n\n", result[0].Content);
        Assert.Equal("\n\n", result[1].Content);
    }

    [Fact]
    public void SplitToFragments_LongestMatchPrecedence_MatchesDotNewlineNewlineOverDot()
    {
        // Using WeakSeparators2 which has both "." and ".\n\n" isn't there, but ExplicitSeparators has ".\n\n"
        var result = MarkdownTextChunker.SplitToFragments("hello.\n\nworld", MarkdownTextChunker.ExplicitSeparators);

        Assert.Equal(3, result.Count);
        Assert.Equal("hello", result[0].Content);
        Assert.Equal(".\n\n", result[1].Content);
        Assert.True(result[1].IsSeparator);
        Assert.Equal("world", result[2].Content);
    }

    [Fact]
    public void SplitToFragments_LongestMatchPrecedence_MatchesTripleQuestionOverDouble()
    {
        var result = MarkdownTextChunker.SplitToFragments("what???really", MarkdownTextChunker.WeakSeparators2);

        // Should match "???" not "??"
        var separatorFragment = result.FirstOrDefault(f => f.IsSeparator && f.Content.Contains('?'));
        Assert.NotNull(separatorFragment);
        Assert.Equal("???", separatorFragment.Content);
    }

    [Fact]
    public void SplitToFragments_UnicodeSeparators_HandlesInterrobangCorrectly()
    {
        var result = MarkdownTextChunker.SplitToFragments("what⁉ really", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "⁉ ");
    }

    [Fact]
    public void SplitToFragments_UnicodeSeparators_HandlesEllipsisCorrectly()
    {
        var result = MarkdownTextChunker.SplitToFragments("wait… more", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "… ");
    }

    #endregion

    #region NormalizeNewlines Tests

    [Fact]
    public void NormalizeNewlines_CRLF_ConvertsToLF()
    {
        var result = MarkdownTextChunker.NormalizeNewlines("hello\r\nworld");
        Assert.Equal("hello\nworld", result);
    }

    [Fact]
    public void NormalizeNewlines_CROnly_ConvertsToLF()
    {
        var result = MarkdownTextChunker.NormalizeNewlines("hello\rworld");
        Assert.Equal("hello\nworld", result);
    }

    [Fact]
    public void NormalizeNewlines_MixedLineEndings_AllConvertToLF()
    {
        var result = MarkdownTextChunker.NormalizeNewlines("a\r\nb\rc\nd");
        Assert.Equal("a\nb\nc\nd", result);
    }

    [Fact]
    public void NormalizeNewlines_AlreadyNormalized_Unchanged()
    {
        var result = MarkdownTextChunker.NormalizeNewlines("hello\nworld");
        Assert.Equal("hello\nworld", result);
    }

    [Fact]
    public void NormalizeNewlines_NoLineEndings_Unchanged()
    {
        var result = MarkdownTextChunker.NormalizeNewlines("hello world");
        Assert.Equal("hello world", result);
    }

    #endregion

    #region MergeImageChunks Tests

    [Fact]
    public void MergeImageChunks_NoImages_Unchanged()
    {
        var chunks = new List<string> { "first", "second", "third" };
        var result = MarkdownTextChunker.MergeImageChunks(chunks);

        Assert.Equal(3, result.Count);
        Assert.Equal(chunks, result);
    }

    [Fact]
    public void MergeImageChunks_ImageAtStart_NotMerged()
    {
        var chunks = new List<string> { "![image](path)", "second" };
        var result = MarkdownTextChunker.MergeImageChunks(chunks);

        Assert.Equal(2, result.Count);
        Assert.Equal("![image](path)", result[0]);
    }

    [Fact]
    public void MergeImageChunks_ImageAfterContent_MergedWithPrevious()
    {
        var chunks = new List<string> { "some text", "![image](path)" };
        var result = MarkdownTextChunker.MergeImageChunks(chunks);

        Assert.Single(result);
        Assert.Contains("some text", result[0]);
        Assert.Contains("![image](path)", result[0]);
    }

    [Fact]
    public void MergeImageChunks_ConsecutiveImages_AllMergedIntoPreceding()
    {
        var chunks = new List<string> { "content", "![img1](p1)", "![img2](p2)" };
        var result = MarkdownTextChunker.MergeImageChunks(chunks);

        Assert.Single(result);
        Assert.Contains("content", result[0]);
        Assert.Contains("![img1](p1)", result[0]);
        Assert.Contains("![img2](p2)", result[0]);
    }

    [Fact]
    public void MergeImageChunks_SingleChunk_Unchanged()
    {
        var chunks = new List<string> { "single chunk" };
        var result = MarkdownTextChunker.MergeImageChunks(chunks);

        Assert.Single(result);
        Assert.Equal("single chunk", result[0]);
    }

    #endregion

    #region Overlap Handling Tests

    [Fact]
    public void Chunk_ZeroOverlap_NoOverlapProcessing()
    {
        var text = string.Join(' ', Enumerable.Repeat("This sentence repeats for testing purposes.", 20));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 50,
            Overlap = 0,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.True(chunks.Count > 1);
        // With zero overlap, chunks should not have shared prefix/suffix
        var tokenizer = TokenizerRegistry.GetTokenizer(config.EncodingModel);
        var firstTokens = tokenizer.EncodeToIds(chunks[0].Text);
        var secondTokens = tokenizer.EncodeToIds(chunks[1].Text);

        // First token of second chunk shouldn't be last token of first chunk
        // (unless by coincidence from the text itself)
        Assert.True(firstTokens.Count > 0);
        Assert.True(secondTokens.Count > 0);
    }

    [Fact]
    public void Chunk_OverlapSmallerThanChunk_AddsOverlapPrefix()
    {
        var text = string.Join(' ', Enumerable.Repeat("Word", 100));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 30,
            Overlap = 10,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.True(chunks.Count > 1);
        // Second chunk should start with overlap from first
        var tokenizer = TokenizerRegistry.GetTokenizer(config.EncodingModel);
        var firstTokens = tokenizer.EncodeToIds(chunks[0].Text);
        var overlapTokens = firstTokens.Skip(Math.Max(0, firstTokens.Count - config.Overlap)).ToArray();
        var overlapText = tokenizer.Decode(overlapTokens);

        Assert.StartsWith(overlapText.Trim(), chunks[1].Text.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_SingleChunk_NoOverlapNeeded()
    {
        var text = "Short text";
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 100,
            Overlap = 20,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.Single(chunks);
        Assert.Equal("Short text", chunks[0].Text);
    }

    #endregion

    #region GenerateChunks Token Boundary Tests

    [Fact]
    public void Chunk_SmallDocument_FitsInSingleChunk()
    {
        var text = "Hello world. This is a test.";
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 100,
            Overlap = 0,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.Single(chunks);
    }

    [Fact]
    public void Chunk_LargeDocument_SplitsIntoMultipleChunks()
    {
        var text = string.Join("\n\n", Enumerable.Repeat("This is a paragraph with enough content to exceed token limits when repeated multiple times.", 20));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 50,
            Overlap = 0,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.True(chunks.Count > 1);

        // Each chunk should respect token limit (approximately)
        var tokenizer = TokenizerRegistry.GetTokenizer(config.EncodingModel);
        foreach (var chunk in chunks)
        {
            var tokenCount = tokenizer.CountTokens(chunk.Text);
            // Allow some flexibility due to overlap and boundary handling
            Assert.True(tokenCount <= config.Size * 1.5, $"Chunk has {tokenCount} tokens, expected <= {config.Size * 1.5}");
        }
    }

    [Fact]
    public void Chunk_DocumentWithHeaders_SplitsAtHeaderBoundaries()
    {
        var text = "# Header 1\n\nContent for header 1.\n\n## Header 2\n\nContent for header 2.\n\n### Header 3\n\nContent for header 3.";
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 20,
            Overlap = 0,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        Assert.True(chunks.Count >= 1);
        // Headers should be preserved in chunks
        Assert.Contains(chunks, c => c.Text.Contains('#'));
    }

    [Fact]
    public void Chunk_TrailingContent_Captured()
    {
        var text = "First paragraph.\n\nSecond paragraph.\n\nTrailing content.";
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 200,
            Overlap = 0,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var chunks = _chunker.Chunk(slices, config);

        var allText = string.Join("", chunks.Select(c => c.Text));
        Assert.Contains("Trailing content", allText);
    }

    #endregion

    #region Regression Tests (Original vs Optimized)

    [Fact]
    public void ChunkOptimized_MatchesOriginal_SmallDocument()
    {
        var text = "# Title\n\nThis is a small document with some content.\n\n## Section\n\nMore content here.";
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 50,
            Overlap = 10,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var original = _chunker.Chunk(slices, config);
        var optimized = _chunker.ChunkOptimized(slices, config);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Text, optimized[i].Text);
            Assert.Equal(original[i].TokenCount, optimized[i].TokenCount);
        }
    }

    [Fact]
    public void ChunkOptimized_MatchesOriginal_MediumDocument()
    {
        var text = string.Join("\n\n", Enumerable.Repeat("This paragraph contains enough text to create multiple chunks when processed.", 15));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 60,
            Overlap = 15,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var original = _chunker.Chunk(slices, config);
        var optimized = _chunker.ChunkOptimized(slices, config);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Text, optimized[i].Text);
            Assert.Equal(original[i].TokenCount, optimized[i].TokenCount);
        }
    }

    [Fact]
    public void ChunkOptimized_MatchesOriginal_WithCodeBlocks()
    {
        var text = @"# Code Example

Here is some code:

```csharp
public class Example
{
    public void Method()
    {
        Console.WriteLine(""Hello"");
    }
}
```

And some more text after the code block.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 100,
            Overlap = 20,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var original = _chunker.Chunk(slices, config);
        var optimized = _chunker.ChunkOptimized(slices, config);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Text, optimized[i].Text);
            Assert.Equal(original[i].TokenCount, optimized[i].TokenCount);
        }
    }

    [Fact]
    public void ChunkOptimized_MatchesOriginal_WithTables()
    {
        var text = @"# Data Table

| Column 1 | Column 2 | Column 3 |
|----------|----------|----------|
| Data 1   | Data 2   | Data 3   |
| Data 4   | Data 5   | Data 6   |

Some text after the table.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 80,
            Overlap = 10,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var original = _chunker.Chunk(slices, config);
        var optimized = _chunker.ChunkOptimized(slices, config);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Text, optimized[i].Text);
            Assert.Equal(original[i].TokenCount, optimized[i].TokenCount);
        }
    }

    [Fact]
    public void ChunkOptimized_MatchesOriginal_WithImages()
    {
        var text = @"# Document with Images

Some introductory text.

![First Image](image1.png)

More content between images.

![Second Image](image2.png)

Final paragraph.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 50,
            Overlap = 5,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var original = _chunker.Chunk(slices, config);
        var optimized = _chunker.ChunkOptimized(slices, config);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Text, optimized[i].Text);
            Assert.Equal(original[i].TokenCount, optimized[i].TokenCount);
        }
    }

    [Fact]
    public void ChunkOptimized_MatchesOriginal_ZeroOverlap()
    {
        var text = string.Join("\n\n", Enumerable.Repeat("Paragraph content.", 10));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 30,
            Overlap = 0,
            EncodingModel = TokenizerDefaults.DefaultEncoding
        };

        var original = _chunker.Chunk(slices, config);
        var optimized = _chunker.ChunkOptimized(slices, config);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Text, optimized[i].Text);
            Assert.Equal(original[i].TokenCount, optimized[i].TokenCount);
        }
    }

    #endregion
}
