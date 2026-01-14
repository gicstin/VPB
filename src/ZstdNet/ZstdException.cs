using System;

namespace ZstdNet
{
    public class ZstdException : Exception
    {
        public ZSTD_ErrorCode Code { get; private set; }

        public ZstdException(ZSTD_ErrorCode code, string message) : base(message)
        {
            Code = code;
        }
    }
}
