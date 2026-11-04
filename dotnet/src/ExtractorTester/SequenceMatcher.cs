namespace ExtractorTester.Compare;

/// <summary>A matching block: a[A..A+Size] == b[B..B+Size].</summary>
public readonly record struct MatchingBlock(int A, int B, int Size);

/// <summary>An edit opcode as produced by difflib's get_opcodes.</summary>
public readonly record struct Opcode(string Tag, int I1, int I2, int J1, int J2);

/// <summary>
/// Port of CPython 3.12 difflib.SequenceMatcher with isjunk=None and autojunk=False.
/// </summary>
public sealed class SequenceMatcher<T> where T : notnull
{
    private readonly IReadOnlyList<T> _a;
    private readonly IReadOnlyList<T> _b;
    private readonly Dictionary<T, List<int>> _b2j = new();
    private List<MatchingBlock>? _blocks;

    public SequenceMatcher(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        _a = a;
        _b = b;
        // Ascending index lists per element, as in difflib.__chain_b.
        for (var i = 0; i < b.Count; i++)
        {
            if (!_b2j.TryGetValue(b[i], out var list))
            {
                list = new List<int>();
                _b2j[b[i]] = list;
            }
            list.Add(i);
        }
    }

    public MatchingBlock FindLongestMatch(int alo, int ahi, int blo, int bhi)
    {
        int besti = alo, bestj = blo, bestsize = 0;
        var j2len = new Dictionary<int, int>();
        for (var i = alo; i < ahi; i++)
        {
            var newj2len = new Dictionary<int, int>();
            if (_b2j.TryGetValue(_a[i], out var js))
            {
                foreach (var j in js)
                {
                    if (j < blo) continue;
                    if (j >= bhi) break;
                    j2len.TryGetValue(j - 1, out var prev);
                    var k = prev + 1;
                    newj2len[j] = k;
                    if (k > bestsize)
                    {
                        besti = i - k + 1;
                        bestj = j - k + 1;
                        bestsize = k;
                    }
                }
            }
            j2len = newj2len;
        }
        // No junk elements, so difflib's junk-extension loops are no-ops.
        return new MatchingBlock(besti, bestj, bestsize);
    }

    public IReadOnlyList<MatchingBlock> GetMatchingBlocks()
    {
        if (_blocks != null) return _blocks;
        int la = _a.Count, lb = _b.Count;
        var queue = new Stack<(int Alo, int Ahi, int Blo, int Bhi)>();
        queue.Push((0, la, 0, lb));
        var matching = new List<MatchingBlock>();
        while (queue.Count > 0)
        {
            var (alo, ahi, blo, bhi) = queue.Pop();
            var m = FindLongestMatch(alo, ahi, blo, bhi);
            if (m.Size == 0) continue;
            matching.Add(m);
            if (alo < m.A && blo < m.B) queue.Push((alo, m.A, blo, m.B));
            if (m.A + m.Size < ahi && m.B + m.Size < bhi)
                queue.Push((m.A + m.Size, ahi, m.B + m.Size, bhi));
        }
        matching.Sort((x, y) =>
        {
            var c = x.A.CompareTo(y.A);
            if (c != 0) return c;
            c = x.B.CompareTo(y.B);
            return c != 0 ? c : x.Size.CompareTo(y.Size);
        });

        var result = new List<MatchingBlock>();
        int i1 = 0, j1 = 0, k1 = 0;
        foreach (var (i2, j2, k2) in matching)
        {
            if (i1 + k1 == i2 && j1 + k1 == j2)
            {
                k1 += k2;
            }
            else
            {
                if (k1 > 0) result.Add(new MatchingBlock(i1, j1, k1));
                i1 = i2; j1 = j2; k1 = k2;
            }
        }
        if (k1 > 0) result.Add(new MatchingBlock(i1, j1, k1));
        result.Add(new MatchingBlock(la, lb, 0));
        _blocks = result;
        return result;
    }

    public IReadOnlyList<Opcode> GetOpcodes()
    {
        int i = 0, j = 0;
        var answer = new List<Opcode>();
        foreach (var (ai, bj, size) in GetMatchingBlocks())
        {
            var tag = "";
            if (i < ai && j < bj) tag = "replace";
            else if (i < ai) tag = "delete";
            else if (j < bj) tag = "insert";
            if (tag.Length > 0) answer.Add(new Opcode(tag, i, ai, j, bj));
            i = ai + size;
            j = bj + size;
            if (size > 0) answer.Add(new Opcode("equal", ai, i, bj, j));
        }
        return answer;
    }

    public double Ratio()
    {
        var matches = 0;
        foreach (var b in GetMatchingBlocks()) matches += b.Size;
        var length = _a.Count + _b.Count;
        return length > 0 ? 2.0 * matches / length : 1.0;
    }
}
