using CodexThreadReader.Core;

namespace CodexThreadReader.Tests;

public sealed class ChatNavigationTests
{
    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(8, 10, 9)]
    [InlineData(9, 10, 9)]
    [InlineData(-1, 0, -1)]
    public void Next_clamps_to_available_message_range(int current, int count, int expected)
    {
        Assert.Equal(expected, ChatNavigation.Next(current, count));
    }

    [Theory]
    [InlineData(9, 10, 8)]
    [InlineData(1, 10, 0)]
    [InlineData(0, 10, 0)]
    [InlineData(-1, 0, -1)]
    public void Previous_clamps_to_available_message_range(int current, int count, int expected)
    {
        Assert.Equal(expected, ChatNavigation.Previous(current, count));
    }
}
