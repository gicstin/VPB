using System;

public static class BoyerMooreHorspool
{
    public static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle == null || haystack == null) return -1;
        int hlen = haystack.Length;
        int nlen = needle.Length;
        if (nlen == 0) return 0;
        if (nlen > hlen) return -1;

        int[] skip = new int[256];
        for (int i = 0; i < skip.Length; i++)
            skip[i] = nlen;

        for (int i = 0; i < nlen - 1; i++)
            skip[needle[i]] = nlen - i - 1;

        int pos = 0;
        while (pos <= hlen - nlen)
        {
            int j = nlen - 1;
            while (j >= 0 && needle[j] == haystack[pos + j])
                j--;

            if (j < 0)
                return pos;

            pos += skip[haystack[pos + nlen - 1]];
        }

        return -1;
    }
}
