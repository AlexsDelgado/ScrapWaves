/// <summary>
/// Allocation-free non-negative integer formatter used with TMP_Text.SetCharArray.
/// </summary>
public static class CombatTextFormatter
{
    private static readonly char[] s_suffixes = { 'K', 'M', 'B', 'T' };
    private static readonly long[] s_divisors = { 1_000L, 1_000_000L, 1_000_000_000L, 1_000_000_000_000L };

    public static int Write(long value, bool compactLargeNumbers, char[] destination)
    {
        if (value <= 0 || destination == null || destination.Length == 0)
            return 0;
        if (!compactLargeNumbers || value <= 99_999L)
            return WriteWhole(value, destination, 0, destination.Length);

        int unit = SelectUnit(value);
        long divisor = s_divisors[unit];
        long whole = value / divisor;
        long remainder = value % divisor;
        int tenth = (int)((remainder * 10L + divisor / 2L) / divisor);
        if (tenth == 10)
        {
            whole++;
            tenth = 0;
        }
        if (whole >= 1000L && unit < s_suffixes.Length - 1)
        {
            unit++;
            divisor = s_divisors[unit];
            whole = value / divisor;
            remainder = value % divisor;
            tenth = (int)((remainder * 10L + divisor / 2L) / divisor);
            if (tenth == 10)
            {
                whole++;
                tenth = 0;
            }
        }

        int length = WriteWhole(whole, destination, 0, destination.Length);
        if (whole < 10L && tenth > 0 && length + 2 < destination.Length)
        {
            destination[length++] = '.';
            destination[length++] = (char)('0' + tenth);
        }
        if (length < destination.Length)
            destination[length++] = s_suffixes[unit];
        return length;
    }

    private static int SelectUnit(long value)
    {
        for (int i = s_divisors.Length - 1; i >= 0; i--)
        {
            if (value >= s_divisors[i])
                return i;
        }
        return 0;
    }

    private static int WriteWhole(long value, char[] destination, int start, int capacity)
    {
        int end = start + capacity;
        int cursor = end;
        do
        {
            if (cursor <= start)
                return 0;
            destination[--cursor] = (char)('0' + value % 10L);
            value /= 10L;
        } while (value > 0L);

        int length = end - cursor;
        for (int i = 0; i < length; i++)
            destination[start + i] = destination[cursor + i];
        return length;
    }
}
