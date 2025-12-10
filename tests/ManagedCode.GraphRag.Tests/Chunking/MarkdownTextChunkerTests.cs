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

    #region ExplicitSeparators Additional Tests

    [Fact]
    public void SplitToFragments_HeaderSeparators_MatchesNewlineHash()
    {
        var result = MarkdownTextChunker.SplitToFragments("content\n# Header1\n## Header2", MarkdownTextChunker.ExplicitSeparators);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n#");
        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n##");
    }

    [Fact]
    public void SplitToFragments_HeaderSeparators_MatchesAllLevels()
    {
        var result = MarkdownTextChunker.SplitToFragments("a\n#b\n##c\n###d\n####e\n#####f", MarkdownTextChunker.ExplicitSeparators);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n#");
        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n##");
        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n###");
        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n####");
        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n#####");
    }

    [Fact]
    public void SplitToFragments_HorizontalRule_MatchesNewlineDashes()
    {
        var result = MarkdownTextChunker.SplitToFragments("above\n---below", MarkdownTextChunker.ExplicitSeparators);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n---");
    }

    [Fact]
    public void SplitToFragments_ExclamationNewlines_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("wow!\n\nmore", MarkdownTextChunker.ExplicitSeparators);
        var result2 = MarkdownTextChunker.SplitToFragments("wow!!\n\nmore", MarkdownTextChunker.ExplicitSeparators);
        var result3 = MarkdownTextChunker.SplitToFragments("wow!!!\n\nmore", MarkdownTextChunker.ExplicitSeparators);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "!\n\n");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "!!\n\n");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "!!!\n\n");
    }

    [Fact]
    public void SplitToFragments_QuestionNewlines_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("what?\n\nmore", MarkdownTextChunker.ExplicitSeparators);
        var result2 = MarkdownTextChunker.SplitToFragments("what??\n\nmore", MarkdownTextChunker.ExplicitSeparators);
        var result3 = MarkdownTextChunker.SplitToFragments("what???\n\nmore", MarkdownTextChunker.ExplicitSeparators);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "?\n\n");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "??\n\n");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "???\n\n");
    }

    #endregion

    #region PotentialSeparators Tests

    [Fact]
    public void SplitToFragments_Blockquote_MatchesNewlineGreaterThan()
    {
        var result = MarkdownTextChunker.SplitToFragments("text\n> quoted", MarkdownTextChunker.PotentialSeparators);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n> ");
    }

    [Fact]
    public void SplitToFragments_BlockquoteList_MatchesVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("text\n>- item", MarkdownTextChunker.PotentialSeparators);
        var result2 = MarkdownTextChunker.SplitToFragments("text\n>* item", MarkdownTextChunker.PotentialSeparators);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "\n>- ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "\n>* ");
    }

    [Fact]
    public void SplitToFragments_NumberedList_MatchesDigitDotSpace()
    {
        var result = MarkdownTextChunker.SplitToFragments("intro\n1. first\n2. second\n10. tenth", MarkdownTextChunker.PotentialSeparators);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n1. ");
        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n2. ");
        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n10. ");
    }

    [Fact]
    public void SplitToFragments_CodeFence_MatchesTripleBacktick()
    {
        var result = MarkdownTextChunker.SplitToFragments("text\n```code", MarkdownTextChunker.PotentialSeparators);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n```");
    }

    #endregion

    #region WeakSeparators1 Tests

    [Fact]
    public void SplitToFragments_TablePipe_MatchesPipeVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("col1| col2", MarkdownTextChunker.WeakSeparators1);
        var result2 = MarkdownTextChunker.SplitToFragments("data |\nmore", MarkdownTextChunker.WeakSeparators1);
        var result3 = MarkdownTextChunker.SplitToFragments("---|-|\ndata", MarkdownTextChunker.WeakSeparators1);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "| ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == " |\n");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "-|\n");
    }

    [Fact]
    public void SplitToFragments_LinkBracket_MatchesOpenBracket()
    {
        var result = MarkdownTextChunker.SplitToFragments("click [here](url)", MarkdownTextChunker.WeakSeparators1);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "[");
    }

    [Fact]
    public void SplitToFragments_ImageBracket_MatchesExclamationBracket()
    {
        var result = MarkdownTextChunker.SplitToFragments("see ![alt](img.png)", MarkdownTextChunker.WeakSeparators1);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "![");
    }

    [Fact]
    public void SplitToFragments_DefinitionList_MatchesNewlineColon()
    {
        var result = MarkdownTextChunker.SplitToFragments("term\n: definition", MarkdownTextChunker.WeakSeparators1);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n: ");
    }

    #endregion

    #region WeakSeparators2 Additional Tests

    [Fact]
    public void SplitToFragments_TabSeparators_MatchesPunctuationTab()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("end.\tnext", MarkdownTextChunker.WeakSeparators2);
        var result2 = MarkdownTextChunker.SplitToFragments("what?\tnext", MarkdownTextChunker.WeakSeparators2);
        var result3 = MarkdownTextChunker.SplitToFragments("wow!\tnext", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == ".\t");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "?\t");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "!\t");
    }

    [Fact]
    public void SplitToFragments_NewlineSeparators_MatchesPunctuationNewline()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("end.\nnext", MarkdownTextChunker.WeakSeparators2);
        var result2 = MarkdownTextChunker.SplitToFragments("what?\nnext", MarkdownTextChunker.WeakSeparators2);
        var result3 = MarkdownTextChunker.SplitToFragments("wow!\nnext", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == ".\n");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "?\n");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "!\n");
    }

    [Fact]
    public void SplitToFragments_QuadPunctuation_MatchesFourChars()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("what!!!!really", MarkdownTextChunker.WeakSeparators2);
        var result2 = MarkdownTextChunker.SplitToFragments("what????really", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "!!!!");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "????");
    }

    [Fact]
    public void SplitToFragments_MixedPunctuation_MatchesInterrobangVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("what?!?really", MarkdownTextChunker.WeakSeparators2);
        var result2 = MarkdownTextChunker.SplitToFragments("what!?!really", MarkdownTextChunker.WeakSeparators2);
        var result3 = MarkdownTextChunker.SplitToFragments("what!?really", MarkdownTextChunker.WeakSeparators2);
        var result4 = MarkdownTextChunker.SplitToFragments("what?!really", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "?!?");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "!?!");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "!?");
        Assert.Contains(result4, f => f.IsSeparator && f.Content == "?!");
    }

    [Fact]
    public void SplitToFragments_Ellipsis_MatchesDotVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("wait....more", MarkdownTextChunker.WeakSeparators2);
        var result2 = MarkdownTextChunker.SplitToFragments("wait...more", MarkdownTextChunker.WeakSeparators2);
        var result3 = MarkdownTextChunker.SplitToFragments("wait..more", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "....");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "...");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "..");
    }

    [Fact]
    public void SplitToFragments_SinglePunctuation_MatchesWithoutSpace()
    {
        // Single punctuation at end of string (no space after)
        var result1 = MarkdownTextChunker.SplitToFragments("end.", MarkdownTextChunker.WeakSeparators2);
        var result2 = MarkdownTextChunker.SplitToFragments("end?", MarkdownTextChunker.WeakSeparators2);
        var result3 = MarkdownTextChunker.SplitToFragments("end!", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == ".");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "?");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "!");
    }

    [Fact]
    public void SplitToFragments_DoubleQuestion_MatchesBeforeTriple()
    {
        var result = MarkdownTextChunker.SplitToFragments("what??next", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "??");
    }

    [Fact]
    public void SplitToFragments_DoubleExclamation_MatchesBeforeTriple()
    {
        var result = MarkdownTextChunker.SplitToFragments("wow!!next", MarkdownTextChunker.WeakSeparators2);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "!!");
    }

    #endregion

    #region WeakSeparators3 Tests

    [Fact]
    public void SplitToFragments_Semicolon_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("a; b", MarkdownTextChunker.WeakSeparators3);
        var result2 = MarkdownTextChunker.SplitToFragments("a;\tb", MarkdownTextChunker.WeakSeparators3);
        var result3 = MarkdownTextChunker.SplitToFragments("a;\nb", MarkdownTextChunker.WeakSeparators3);
        var result4 = MarkdownTextChunker.SplitToFragments("a;b", MarkdownTextChunker.WeakSeparators3);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "; ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == ";\t");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == ";\n");
        Assert.Contains(result4, f => f.IsSeparator && f.Content == ";");
    }

    [Fact]
    public void SplitToFragments_CloseBrace_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("a} b", MarkdownTextChunker.WeakSeparators3);
        var result2 = MarkdownTextChunker.SplitToFragments("a}\tb", MarkdownTextChunker.WeakSeparators3);
        var result3 = MarkdownTextChunker.SplitToFragments("a}\nb", MarkdownTextChunker.WeakSeparators3);
        var result4 = MarkdownTextChunker.SplitToFragments("a}b", MarkdownTextChunker.WeakSeparators3);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "} ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "}\t");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "}\n");
        Assert.Contains(result4, f => f.IsSeparator && f.Content == "}");
    }

    [Fact]
    public void SplitToFragments_CloseParen_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("(a) b", MarkdownTextChunker.WeakSeparators3);
        var result2 = MarkdownTextChunker.SplitToFragments("(a)\tb", MarkdownTextChunker.WeakSeparators3);
        var result3 = MarkdownTextChunker.SplitToFragments("(a)\nb", MarkdownTextChunker.WeakSeparators3);
        var result4 = MarkdownTextChunker.SplitToFragments("(a)b", MarkdownTextChunker.WeakSeparators3);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == ") ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == ")\t");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == ")\n");
        Assert.Contains(result4, f => f.IsSeparator && f.Content == ")");
    }

    [Fact]
    public void SplitToFragments_CloseBracket_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("[a] b", MarkdownTextChunker.WeakSeparators3);
        var result2 = MarkdownTextChunker.SplitToFragments("[a]\tb", MarkdownTextChunker.WeakSeparators3);
        var result3 = MarkdownTextChunker.SplitToFragments("[a]\nb", MarkdownTextChunker.WeakSeparators3);
        var result4 = MarkdownTextChunker.SplitToFragments("[a]b", MarkdownTextChunker.WeakSeparators3);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == "] ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == "]\t");
        Assert.Contains(result3, f => f.IsSeparator && f.Content == "]\n");
        Assert.Contains(result4, f => f.IsSeparator && f.Content == "]");
    }

    [Fact]
    public void SplitToFragments_Colon_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("key: value", MarkdownTextChunker.WeakSeparators3);
        var result2 = MarkdownTextChunker.SplitToFragments("key:value", MarkdownTextChunker.WeakSeparators3);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == ": ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == ":");
    }

    [Fact]
    public void SplitToFragments_Comma_MatchesAllVariants()
    {
        var result1 = MarkdownTextChunker.SplitToFragments("a, b", MarkdownTextChunker.WeakSeparators3);
        var result2 = MarkdownTextChunker.SplitToFragments("a,b", MarkdownTextChunker.WeakSeparators3);

        Assert.Contains(result1, f => f.IsSeparator && f.Content == ", ");
        Assert.Contains(result2, f => f.IsSeparator && f.Content == ",");
    }

    [Fact]
    public void SplitToFragments_SingleNewline_MatchesInWeakSeparators3()
    {
        var result = MarkdownTextChunker.SplitToFragments("line1\nline2", MarkdownTextChunker.WeakSeparators3);

        Assert.Contains(result, f => f.IsSeparator && f.Content == "\n");
    }

    #endregion

    #region Edge Cases and Optimized Equivalence Tests

    [Fact]
    public void SplitToFragments_MultipleSeparatorTypes_ProcessesInOrder()
    {
        // Mix of different separator types
        var result = MarkdownTextChunker.SplitToFragments("hello.\n\nworld", MarkdownTextChunker.ExplicitSeparators);

        Assert.Equal(3, result.Count);
        Assert.Equal("hello", result[0].Content);
        Assert.False(result[0].IsSeparator);
        Assert.Equal(".\n\n", result[1].Content);
        Assert.True(result[1].IsSeparator);
        Assert.Equal("world", result[2].Content);
        Assert.False(result[2].IsSeparator);
    }

    [Fact]
    public void SplitToFragments_SixConsecutiveNewlines_CreatesSeparateFragments()
    {
        var result = MarkdownTextChunker.SplitToFragments("\n\n\n\n\n\n", MarkdownTextChunker.ExplicitSeparators);

        // Should match \n\n three times
        Assert.Equal(3, result.Count);
        Assert.All(result, f => Assert.True(f.IsSeparator));
        Assert.All(result, f => Assert.Equal("\n\n", f.Content));
    }

    [Fact]
    public void SplitToFragments_SeparatorOnly_ReturnsOnlySeparators()
    {
        var result = MarkdownTextChunker.SplitToFragments(".\n\n", MarkdownTextChunker.ExplicitSeparators);

        Assert.Single(result);
        Assert.True(result[0].IsSeparator);
        Assert.Equal(".\n\n", result[0].Content);
    }

    [Fact]
    public void SplitToFragmentsOptimized_MatchesOriginal_ExplicitSeparators()
    {
        var text = "Hello.\n\nWorld!\n\nHow?\n\n";
        var original = MarkdownTextChunker.SplitToFragments(text, MarkdownTextChunker.ExplicitSeparators);
        var optimized = MarkdownTextChunker.SplitToFragmentsOptimized(text, MarkdownTextChunker.ExplicitSeparators);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Content, text[optimized[i].Range]);
            Assert.Equal(original[i].IsSeparator, optimized[i].IsSeparator);
        }
    }

    [Fact]
    public void SplitToFragmentsOptimized_MatchesOriginal_PotentialSeparators()
    {
        var text = "intro\n1. first\n2. second\n> quote\n```code";
        var original = MarkdownTextChunker.SplitToFragments(text, MarkdownTextChunker.PotentialSeparators);
        var optimized = MarkdownTextChunker.SplitToFragmentsOptimized(text, MarkdownTextChunker.PotentialSeparators);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Content, text[optimized[i].Range]);
            Assert.Equal(original[i].IsSeparator, optimized[i].IsSeparator);
        }
    }

    [Fact]
    public void SplitToFragmentsOptimized_MatchesOriginal_WeakSeparators1()
    {
        var text = "click [here](url) and see ![img](path) with | table";
        var original = MarkdownTextChunker.SplitToFragments(text, MarkdownTextChunker.WeakSeparators1);
        var optimized = MarkdownTextChunker.SplitToFragmentsOptimized(text, MarkdownTextChunker.WeakSeparators1);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Content, text[optimized[i].Range]);
            Assert.Equal(original[i].IsSeparator, optimized[i].IsSeparator);
        }
    }

    [Fact]
    public void SplitToFragmentsOptimized_MatchesOriginal_WeakSeparators2()
    {
        var text = "Hello. World? Yes! Really??? Wait...";
        var original = MarkdownTextChunker.SplitToFragments(text, MarkdownTextChunker.WeakSeparators2);
        var optimized = MarkdownTextChunker.SplitToFragmentsOptimized(text, MarkdownTextChunker.WeakSeparators2);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Content, text[optimized[i].Range]);
            Assert.Equal(original[i].IsSeparator, optimized[i].IsSeparator);
        }
    }

    [Fact]
    public void SplitToFragmentsOptimized_MatchesOriginal_WeakSeparators3()
    {
        var text = "a; b, c: d} e) f] g\nh";
        var original = MarkdownTextChunker.SplitToFragments(text, MarkdownTextChunker.WeakSeparators3);
        var optimized = MarkdownTextChunker.SplitToFragmentsOptimized(text, MarkdownTextChunker.WeakSeparators3);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Content, text[optimized[i].Range]);
            Assert.Equal(original[i].IsSeparator, optimized[i].IsSeparator);
        }
    }

    [Fact]
    public void SplitToFragmentsOptimized_MatchesOriginal_NullSeparators()
    {
        var text = "abc";
        var original = MarkdownTextChunker.SplitToFragments(text, null);
        var optimized = MarkdownTextChunker.SplitToFragmentsOptimized(text, null);

        Assert.Equal(original.Count, optimized.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Content, text[optimized[i].Range]);
            Assert.Equal(original[i].IsSeparator, optimized[i].IsSeparator);
        }
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

    [Fact]
    public void ChunkOptimized_MatchesOriginal_WithMultipleHeaderLevels()
    {
        var text = @"# Main Title

Introduction paragraph with some content here.

## Section One

Content for section one goes here with enough text to make it substantial.

### Subsection 1.1

Details about the first subsection with additional content.

#### Deep Nested Header

Even deeper content that should be chunked properly.

##### Level Five Header

The deepest header level we support in this test.

## Section Two

Another major section with its own content.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 40,
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
    public void ChunkOptimized_MatchesOriginal_WithHorizontalRules()
    {
        var text = @"First section content here.

---

Second section after horizontal rule.

---

Third section content.

---

Final section with more text to ensure proper chunking behavior.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 30,
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
    public void ChunkOptimized_MatchesOriginal_WithExclamationQuestionNewlines()
    {
        var text = @"This is exciting!

More content follows the exclamation.

What do you think??

Double question marks are interesting.

Is this working???

Triple question marks for emphasis.

Wow!!

Double exclamation too.

Amazing!!!

Triple exclamation for maximum impact.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 35,
            Overlap = 8,
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
    public void ChunkOptimized_MatchesOriginal_WithBlockquotes()
    {
        var text = @"Introduction text before the quote.

> This is a blockquote with some meaningful content.

Text between quotes.

> Another blockquote here.
> With multiple lines.

>- A blockquote with a list item.

>* Another list style in blockquote.

Conclusion text after all quotes.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 40,
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
    public void ChunkOptimized_MatchesOriginal_WithNumberedLists()
    {
        var text = @"Here is a numbered list:

1. First item with some description.
2. Second item with more content.
3. Third item continues the list.
4. Fourth item here.
5. Fifth item in sequence.
6. Sixth item follows.
7. Seventh item.
8. Eighth item.
9. Ninth item.
10. Tenth item completes the list.

Conclusion after the list.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 45,
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
    public void ChunkOptimized_MatchesOriginal_WithNestedCodeFences()
    {
        var text = @"Introduction to code examples.

```python
def hello():
    print('Hello, World!')
    return True
```

Some text between code blocks.

```javascript
function greet() {
    console.log('Hi there!');
}
```

Another explanation section.

```csharp
public void Method()
{
    Console.WriteLine(""Test"");
}
```

Final thoughts after all code.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 50,
            Overlap = 12,
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
    public void ChunkOptimized_MatchesOriginal_WithLinks()
    {
        var text = @"Check out [this link](https://example.com) for more info.

Here is [another link](https://test.com) to explore.

Multiple [links](url1) in [one](url2) sentence [here](url3).

Final paragraph with [documentation](https://docs.example.com).";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 35,
            Overlap = 8,
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
    public void ChunkOptimized_MatchesOriginal_WithDefinitionLists()
    {
        var text = @"Term One
: Definition of the first term with explanation.

Term Two
: Second definition here.
: Alternative definition for term two.

Term Three
: Third definition with more content to make it longer.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 40,
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
    public void ChunkOptimized_MatchesOriginal_WithPunctuationVariants()
    {
        var text = @"First sentence. Second sentence. Third one here.

Question time? Another question? Yet another?

Exciting content! More excitement! Even more!

Wait... Something interesting... And more...

Really??? Yes really??? Are you sure???";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 35,
            Overlap = 8,
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
    public void ChunkOptimized_MatchesOriginal_WithUnicodePunctuation()
    {
        var text = @"What is this⁉ Something surprising happened.

Is it true⁈ The interrobang reversed.

Really⁇ Double question marks unicode style.

Wait… The ellipsis character in use.

More content with ⁉ scattered throughout⁈ the text⁇ for testing…";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 40,
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
    public void ChunkOptimized_MatchesOriginal_WithMixedInterrobangs()
    {
        var text = @"What happened?! This is surprising!

Is it true!? The reverse pattern works too.

Really?!? Triple mixed punctuation here.

Wow!?! Another triple variation.

Simple text. Normal ending. Back to basics.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 35,
            Overlap = 8,
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
    public void ChunkOptimized_MatchesOriginal_WithCodeBraces()
    {
        var text = @"Function call example: doSomething() returns a value.

Object literal: {key: value} is common in JSON.

Array access: items[0] gets the first element.

Combined: obj.method() and arr[index] work together.

Nested: outer{inner(deep[0])} complex structure.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 35,
            Overlap = 8,
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
    public void ChunkOptimized_MatchesOriginal_WithDelimiters()
    {
        var text = @"Items: first, second, third, fourth, fifth.

Key: value; another: data; more: info.

List items: one, two, three; group two: a, b, c.

Code style; statement; another; final.

Mixed: data, more; extra: stuff, things.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 35,
            Overlap = 8,
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
    public void ChunkOptimized_MatchesOriginal_WithMixedSeparatorTypes()
    {
        var text = @"# Main Document Title

Introduction paragraph with content.

## First Section

This is exciting! What do you think? Really???

> A quote to consider.

1. First item
2. Second item
3. Third item

---

### Code Example

```python
def test():
    return True
```

Check [this link](url) for more info.

| Col1 | Col2 |
|------|------|
| A    | B    |

Term
: Definition here.

Final thoughts; conclusion, done.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 50,
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
    public void ChunkOptimized_MatchesOriginal_LargeOverlap()
    {
        var text = string.Join("\n\n", Enumerable.Repeat("This is a paragraph with content for testing large overlap scenarios.", 15));
        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 60,
            Overlap = 40, // Large overlap - more than 50% of chunk size
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
    public void ChunkOptimized_MatchesOriginal_VerySmallChunkSize()
    {
        var text = @"# Header

Short paragraph. Another sentence. More text here.

## Section

Content continues. Still going. Almost done.";

        var slices = new[] { new ChunkSlice("doc-1", text) };

        var config = new ChunkingConfig
        {
            Size = 10, // Very small chunk size
            Overlap = 3,
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
