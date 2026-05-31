namespace CodexThreadReader.Core;

public static class ChatNavigation
{
    public static int Previous(int currentIndex, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        return Math.Max(0, currentIndex - 1);
    }

    public static int Next(int currentIndex, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        return Math.Min(count - 1, Math.Max(0, currentIndex) + 1);
    }
}
