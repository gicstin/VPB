using System;
using UnityEngine;
using var_browser;

public class Reproduction
{
    public static void Run()
    {
        TestParse("`", "BackQuote");
        TestParse("1", "Alpha1");
        TestParse("a", "A");
        TestParse("Ctrl+V", "V");
        TestParse("Shift+`", "BackQuote");
    }

    private static void TestParse(string input, string expectedKeyName)
    {
        try
        {
            var result = KeyUtil.Parse(input);
            Console.WriteLine($"Parse('{input}'): Success. Key={result.key}, Pattern={result.keyPattern}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Parse('{input}'): Failed. Error: {ex.Message}");
        }
    }
}
