using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;

namespace HW.UnitTests.Domain;

public class WordTests
{
    [Fact]
    public void Create_keeps_the_value()
    {
        var word = Word.Create("serendipity");

        Assert.Equal("serendipity", word.Value);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        var word = Word.Create("  serendipity \r\n");

        Assert.Equal("serendipity", word.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_rejects_a_word_that_is_not_there(string? value)
    {
        var error = Assert.Throws<BadRequestException>(() => Word.Create(value));

        Assert.Equal("Word cannot be empty.", error.Message);
    }

    [Fact]
    public void Create_accepts_a_word_of_exactly_the_maximum_length()
    {
        var word = Word.Create(new string('a', Word.MaxLength));

        Assert.Equal(Word.MaxLength, word.Value.Length);
    }

    [Fact]
    public void Create_rejects_a_word_over_the_maximum_length()
    {
        var error = Assert.Throws<BadRequestException>(() => Word.Create(new string('a', Word.MaxLength + 1)));

        Assert.Equal($"Word cannot exceed {Word.MaxLength} characters.", error.Message);
    }

    [Fact]
    public void Create_measures_length_after_trimming()
    {
        var padded = "  " + new string('a', Word.MaxLength) + "  ";

        var word = Word.Create(padded);

        Assert.Equal(Word.MaxLength, word.Value.Length);
    }

    [Fact]
    public void FromPersistence_takes_the_stored_value_as_it_is()
    {
        // The converter reads back what the database holds; re-validating it there would turn a bad
        // row into an exception on every read instead of on the write that caused it.
        var word = Word.FromPersistence("  already stored  ");

        Assert.Equal("  already stored  ", word.Value);
    }

    [Fact]
    public void Converts_to_string_implicitly_and_via_ToString()
    {
        var word = Word.Create("ephemeral");

        string implicitly = word;

        Assert.Equal("ephemeral", implicitly);
        Assert.Equal("ephemeral", word.ToString());
    }

    [Fact]
    public void Two_words_with_the_same_value_are_equal()
    {
        var left = Word.Create("ubiquitous");
        var right = Word.Create(" ubiquitous ");

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Words_with_different_values_are_not_equal()
    {
        var left = Word.Create("ubiquitous");
        var right = Word.Create("ubiquity");

        Assert.NotEqual(left, right);
        Assert.True(left != right);
    }

    [Fact]
    public void Comparison_is_case_sensitive()
    {
        Assert.NotEqual(Word.Create("Word"), Word.Create("word"));
    }

    [Fact]
    public void A_word_is_never_equal_to_null()
    {
        var word = Word.Create("solitude");

        Assert.False(word.Equals(null));
        Assert.False(word == null);
        Assert.True(word != null);
    }

    [Fact]
    public void Two_nulls_compare_equal()
    {
        Word? left = null;
        Word? right = null;

        Assert.True(left == right);
        Assert.False(left != right);
    }
}
