namespace ExtractorTester.Compare;

/// <summary>
/// Port of rapidfuzz.distance.Indel over code-point arrays: distance, normalized similarity
/// and opcodes (equal/insert/delete). Bit-parallel LCS (Hyyro) with rapidfuzz's backtrack
/// tie-break order, so the first equal opcode matches rapidfuzz.
/// </summary>
public static class Indel
{
    public static int LcsLength(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        Affix(a, b, out var prefix, out var suffix);
        return prefix + suffix + Llcs(a[prefix..^suffix], b[prefix..^suffix], null);
    }

    public static int Distance(ReadOnlySpan<int> a, ReadOnlySpan<int> b) =>
        a.Length + b.Length - 2 * LcsLength(a, b);

    /// <summary>1 - distance / (len(a) + len(b)); 1.0 when both are empty.</summary>
    public static double NormalizedSimilarity(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var total = a.Length + b.Length;
        return total == 0 ? 1.0 : 1.0 - (double)Distance(a, b) / total;
    }

    /// <summary>Opcodes turning a into b; no replace ops (Indel has only insert/delete).</summary>
    public static List<Opcode> Opcodes(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var ops = new List<Opcode>();
        var edits = Editops(a, b);

        int sp = 0, dp = 0, i = 0;
        while (i < edits.Length)
        {
            var (del, src, dest) = edits[i];
            if (sp < src || dp < dest)
            {
                ops.Add(new Opcode("equal", sp, src, dp, dest));
                sp = src;
                dp = dest;
            }
            int sb = sp, db = dp;
            do
            {
                if (del) sp++; else dp++;
                i++;
            } while (i < edits.Length && edits[i].Del == del && edits[i].Src == sp && edits[i].Dest == dp);
            ops.Add(new Opcode(del ? "delete" : "insert", sb, sp, db, dp));
        }
        if (sp < a.Length || dp < b.Length)
            ops.Add(new Opcode("equal", sp, a.Length, dp, b.Length));
        return ops;
    }

    private static (bool Del, int Src, int Dest)[] Editops(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        Affix(a, b, out var prefix, out var suffix);
        var s1 = a[prefix..^suffix];
        var s2 = b[prefix..^suffix];
        var rows = new List<ulong[]>(s2.Length);
        var llcs = Llcs(s1, s2, rows);
        var dist = s1.Length + s2.Length - 2 * llcs;
        var edits = new (bool Del, int Src, int Dest)[dist];

        int col = s1.Length, row = s2.Length;
        while (row > 0 && col > 0)
        {
            if (Test(rows[row - 1], col - 1))
            {
                col--;
                edits[--dist] = (true, col + prefix, row + prefix);
            }
            else
            {
                row--;
                if (row > 0 && !Test(rows[row - 1], col - 1))
                    edits[--dist] = (false, col + prefix, row + prefix);
                else
                    col--;
            }
        }
        while (col > 0)
        {
            col--;
            edits[--dist] = (true, col + prefix, row + prefix);
        }
        while (row > 0)
        {
            row--;
            edits[--dist] = (false, col + prefix, row + prefix);
        }
        return edits;
    }

    private static bool Test(ulong[] words, int bit) => ((words[bit >> 6] >> (bit & 63)) & 1UL) != 0;

    private static void Affix(ReadOnlySpan<int> a, ReadOnlySpan<int> b, out int prefix, out int suffix)
    {
        var min = Math.Min(a.Length, b.Length);
        prefix = 0;
        while (prefix < min && a[prefix] == b[prefix]) prefix++;
        suffix = 0;
        while (suffix < min - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;
    }

    /// <summary>Bit-parallel LLCS of s1 (bit columns) against s2 (rows). Optionally records each row state.</summary>
    private static int Llcs(ReadOnlySpan<int> s1, ReadOnlySpan<int> s2, List<ulong[]>? rows)
    {
        var n = s1.Length;
        var words = (n + 63) / 64;
        if (n == 0 || s2.Length == 0)
        {
            if (rows != null)
                for (var r = 0; r < s2.Length; r++) rows.Add(new ulong[words]);
            return 0;
        }
        var pm = new Dictionary<int, ulong[]>();
        for (var i = 0; i < n; i++)
        {
            if (!pm.TryGetValue(s1[i], out var m)) pm[s1[i]] = m = new ulong[words];
            m[i >> 6] |= 1UL << (i & 63);
        }
        var s = new ulong[words];
        Array.Fill(s, ulong.MaxValue);
        var zero = new ulong[words];
        for (var r = 0; r < s2.Length; r++)
        {
            var m = pm.TryGetValue(s2[r], out var found) ? found : zero;
            ulong carry = 0;
            for (var w = 0; w < words; w++)
            {
                var sw = s[w];
                var u = sw & m[w];
                var sum = sw + u;
                var c1 = sum < sw ? 1UL : 0UL;
                var sum2 = sum + carry;
                var c2 = sum2 < sum ? 1UL : 0UL;
                carry = c1 | c2;
                s[w] = sum2 | (sw & ~m[w]);
            }
            rows?.Add((ulong[])s.Clone());
        }
        var count = 0;
        for (var i = 0; i < n; i++)
            if (!Test(s, i)) count++;
        return count;
    }
}
