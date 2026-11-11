using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>What a condition can ask about the company (ManagerFacts in the game, a stub in the validator).</summary>
public interface IManagerFacts
{
    /// <summary>A number fact ("happiness") or a target number ("emp.profit"). False when it isn't known.</summary>
    bool TryGetNumber(string name, out double value);

    /// <summary>A predicate: kind "flag", "policy", "feature"... with its argument (may be empty: "rental").</summary>
    bool Test(string kind, string arg);
}

/// <summary>
/// A parsed condition string (plain C#). Grammar:
/// <code>
/// expr      := and ( "||" and )*
/// and       := atom ( "&amp;&amp;" atom )*
/// atom      := "!"? ( "(" expr ")" | predicate | comparison )
/// predicate := kind ":" arg | kind        (flag:x, policy:food, rental, personality:people, target:emp ...)
/// comparison:= operand op operand         (op: &lt; &lt;= &gt; &gt;= == !=; operand: a number or a fact / target.fact name)
/// </code>
/// Examples: "happiness &lt; 40", "free &lt;= 1 &amp;&amp; capacity_limited == 1", "!flag:food_upgraded", "emp.profit &gt; low_profit"
/// (no arithmetic: compare facts with facts or numbers). A bare fact name means "!= 0".
/// </summary>
public sealed class ManagerCondition
{
    public readonly string Source;
    readonly Expr root;

    ManagerCondition(string source, Expr root)
    {
        Source = source;
        this.root = root;
    }

    /// <summary>Parse; null with <paramref name="error"/> when the text isn't a valid condition.</summary>
    public static ManagerCondition Parse(string text, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty condition";
            return null;
        }
        try
        {
            var parser = new Parser(Tokenize(text));
            Expr expr = parser.ParseOr();
            if (!parser.AtEnd) throw new FormatException("unexpected '" + parser.Peek + "'");
            return new ManagerCondition(text.Trim(), expr);
        }
        catch (FormatException e)
        {
            error = "\"" + text + "\": " + e.Message;
            return null;
        }
    }

    /// <summary>True when the condition holds. Unknown facts count as 0 (the validator catches misspelled names).</summary>
    public bool Eval(IManagerFacts facts)
    {
        try { return root.Eval(facts); }
        catch (Exception) { return false; }
    }

    /// <summary>Every fact name and predicate the condition uses (for the validator).</summary>
    public void CollectNames(List<string> numbers, List<KeyValuePair<string, string>> predicates)
    {
        root.Collect(numbers, predicates);
    }

    /// <summary>All of <paramref name="conditions"/> hold (an empty or null list holds).</summary>
    public static bool All(List<ManagerCondition> conditions, IManagerFacts facts)
    {
        if (conditions == null) return true;
        foreach (ManagerCondition c in conditions)
            if (c != null && !c.Eval(facts)) return false;
        return true;
    }

    public override string ToString() => Source;

    // ------------------------------------------------------------------

    abstract class Expr
    {
        public abstract bool Eval(IManagerFacts facts);
        public abstract void Collect(List<string> numbers, List<KeyValuePair<string, string>> predicates);
    }

    sealed class Or : Expr
    {
        public readonly List<Expr> Parts = new List<Expr>();
        public override bool Eval(IManagerFacts facts)
        {
            foreach (Expr e in Parts) if (e.Eval(facts)) return true;
            return false;
        }
        public override void Collect(List<string> n, List<KeyValuePair<string, string>> p) { foreach (Expr e in Parts) e.Collect(n, p); }
    }

    sealed class And : Expr
    {
        public readonly List<Expr> Parts = new List<Expr>();
        public override bool Eval(IManagerFacts facts)
        {
            foreach (Expr e in Parts) if (!e.Eval(facts)) return false;
            return true;
        }
        public override void Collect(List<string> n, List<KeyValuePair<string, string>> p) { foreach (Expr e in Parts) e.Collect(n, p); }
    }

    sealed class Not : Expr
    {
        public Expr Inner;
        public override bool Eval(IManagerFacts facts) => !Inner.Eval(facts);
        public override void Collect(List<string> n, List<KeyValuePair<string, string>> p) => Inner.Collect(n, p);
    }

    sealed class Predicate : Expr
    {
        public string Kind, Arg;
        public override bool Eval(IManagerFacts facts) => facts != null && facts.Test(Kind, Arg);
        public override void Collect(List<string> n, List<KeyValuePair<string, string>> p) => p.Add(new KeyValuePair<string, string>(Kind, Arg));
    }

    sealed class Operand
    {
        public bool IsNumber;
        public double Number;
        public string Name;

        public double Value(IManagerFacts facts)
        {
            if (IsNumber) return Number;
            return facts != null && facts.TryGetNumber(Name, out double v) ? v : 0.0;
        }
    }

    sealed class Compare : Expr
    {
        public Operand Left, Right;
        public string Op;
        public override bool Eval(IManagerFacts facts)
        {
            double a = Left.Value(facts), b = Right.Value(facts);
            switch (Op)
            {
                case "<": return a < b;
                case "<=": return a <= b;
                case ">": return a > b;
                case ">=": return a >= b;
                case "==": return Math.Abs(a - b) < 1e-9;
                case "!=": return Math.Abs(a - b) >= 1e-9;
            }
            return false;
        }
        public override void Collect(List<string> n, List<KeyValuePair<string, string>> p)
        {
            if (!Left.IsNumber) n.Add(Left.Name);
            if (!Right.IsNumber) n.Add(Right.Name);
        }
    }

    // ------------------------------------------------------------------

    static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(' || c == ')') { tokens.Add(c.ToString()); i++; continue; }
            if (i + 1 < text.Length)
            {
                string two = text.Substring(i, 2);
                if (two == "&&" || two == "||" || two == "<=" || two == ">=" || two == "==" || two == "!=")
                {
                    tokens.Add(two);
                    i += 2;
                    continue;
                }
            }
            if (c == '<' || c == '>' || c == '!') { tokens.Add(c.ToString()); i++; continue; }
            int start = i;
            while (i < text.Length)
            {
                char d = text[i];
                bool word = char.IsLetterOrDigit(d) || d == '_' || d == '.' || d == ':' || d == '-';
                // A '-' only belongs to a number at its start ("-5"), or inside ids after ':' (flag:my-flag)
                if (!word) break;
                i++;
            }
            if (i == start) throw new FormatException("unexpected character '" + c + "'");
            tokens.Add(text.Substring(start, i - start));
        }
        return tokens;
    }

    sealed class Parser
    {
        readonly List<string> tokens;
        int pos;

        public Parser(List<string> tokens) { this.tokens = tokens; }

        public bool AtEnd => pos >= tokens.Count;
        public string Peek => pos < tokens.Count ? tokens[pos] : "";

        string Next()
        {
            if (AtEnd) throw new FormatException("unexpected end");
            return tokens[pos++];
        }

        public Expr ParseOr()
        {
            var or = new Or();
            or.Parts.Add(ParseAnd());
            while (!AtEnd && Peek == "||")
            {
                pos++;
                or.Parts.Add(ParseAnd());
            }
            return or.Parts.Count == 1 ? or.Parts[0] : or;
        }

        Expr ParseAnd()
        {
            var and = new And();
            and.Parts.Add(ParseAtom());
            while (!AtEnd && Peek == "&&")
            {
                pos++;
                and.Parts.Add(ParseAtom());
            }
            return and.Parts.Count == 1 ? and.Parts[0] : and;
        }

        Expr ParseAtom()
        {
            string t = Next();
            if (t == "!") return new Not { Inner = ParseAtom() };
            if (t == "(")
            {
                Expr inner = ParseOr();
                if (Next() != ")") throw new FormatException("expected ')'");
                return inner;
            }
            if (IsOperator(t) || t == ")" || t == "&&" || t == "||") throw new FormatException("unexpected '" + t + "'");

            // kind:arg predicate
            int colon = t.IndexOf(':');
            if (colon >= 0)
                return new Predicate { Kind = t.Substring(0, colon), Arg = t.Substring(colon + 1) };

            Operand left = ToOperand(t);
            if (!AtEnd && IsOperator(Peek))
            {
                string op = Next();
                string right = Next();
                if (IsOperator(right) || right == "(" || right == ")") throw new FormatException("expected a number or a name after '" + op + "'");
                return new Compare { Left = left, Op = op, Right = ToOperand(right) };
            }
            if (left.IsNumber) throw new FormatException("a number on its own isn't a condition");
            // A bare name: a predicate without an argument ("rental", "share") or a fact that must not be 0
            if (ManagerSchema.Predicates.ContainsKey(left.Name))
                return new Predicate { Kind = left.Name, Arg = "" };
            return new Compare { Left = left, Op = "!=", Right = new Operand { IsNumber = true, Number = 0 } };
        }

        static bool IsOperator(string t) => t == "<" || t == "<=" || t == ">" || t == ">=" || t == "==" || t == "!=";

        static Operand ToOperand(string t)
        {
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                return new Operand { IsNumber = true, Number = n };
            if (t.Length == 0 || !(char.IsLetter(t[0]) || t[0] == '_')) throw new FormatException("'" + t + "' isn't a number or a name");
            return new Operand { Name = t };
        }
    }
}
