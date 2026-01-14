using System;
using size_t = System.UIntPtr;

namespace ZstdNet
{
	public class Decompressor : IDisposable
	{
		public Decompressor()
			: this(new DecompressionOptions(null))
		{}

		public Decompressor(DecompressionOptions options)
		{
			Options = options;
			dctx = ExternMethods.ZSTD_createDCtx().EnsureZstdSuccess();

			options.ApplyDecompressionParams(dctx);
		}

		~Decompressor()
        {
            Dispose(false);
        }

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if(dctx == IntPtr.Zero)
				return;

			ExternMethods.ZSTD_freeDCtx(dctx);

			dctx = IntPtr.Zero;
		}

		public byte[] Unwrap(byte[] src, int maxDecompressedSize = int.MaxValue)
        {
            if (src == null) throw new ArgumentNullException("src");
            return Unwrap(src, 0, src.Length, maxDecompressedSize);
        }

		public byte[] Unwrap(byte[] src, int offset, int length, int maxDecompressedSize = int.MaxValue)
		{
			var expectedDstSize = GetDecompressedSize(src, offset, length);
			if(expectedDstSize > (ulong)maxDecompressedSize)
				throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall, $"Decompressed content size {expectedDstSize} is greater than {maxDecompressedSize}");
			if(expectedDstSize > Consts.MaxByteArrayLength)
				throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall, $"Decompressed content size {expectedDstSize} is greater than max possible byte array size {Consts.MaxByteArrayLength}");

			var dst = new byte[expectedDstSize];

			var dstSize = Unwrap(src, offset, length, dst, 0, dst.Length, false);
			if(expectedDstSize != (ulong)dstSize)
				throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_GENERIC, "Decompressed content size specified in the src data frame is invalid");

			return dst;
		}

		public static ulong GetDecompressedSize(byte[] src)
        {
            if (src == null) throw new ArgumentNullException("src");
            return GetDecompressedSize(src, 0, src.Length);
        }

		public static unsafe ulong GetDecompressedSize(byte[] src, int offset, int length)
		{
            fixed (byte* srcPtr = src)
            {
                IntPtr srcP = (IntPtr)(srcPtr + offset);
			    var size = ExternMethods.ZSTD_getFrameContentSize(srcP, (size_t)length);
			    if(size == ExternMethods.ZSTD_CONTENTSIZE_UNKNOWN)
				    throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_GENERIC, "Decompressed content size is not specified");
			    if(size == ExternMethods.ZSTD_CONTENTSIZE_ERROR)
				    throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_GENERIC, "Decompressed content size cannot be determined (e.g. invalid magic number, srcSize too small)");
			    return size;
            }
		}

		public int Unwrap(byte[] src, byte[] dst, int offset, bool bufferSizePrecheck = true)
        {
             return Unwrap(src, 0, src.Length, dst, offset, dst.Length - offset, bufferSizePrecheck);
        }

		public unsafe int Unwrap(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength, bool bufferSizePrecheck = true)
		{
			if(dstOffset < 0 || dstOffset > dst.Length)
				throw new ArgumentOutOfRangeException("dstOffset");
            if(srcOffset < 0 || srcOffset + srcLength > src.Length)
                throw new ArgumentOutOfRangeException("srcOffset");

			if(bufferSizePrecheck)
			{
				var expectedDstSize = GetDecompressedSize(src, srcOffset, srcLength);
				if(expectedDstSize > (ulong)dstLength)
					throw new ZstdException(ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall, "Destination buffer size is less than specified decompressed content size");
			}

            fixed (byte* srcPtr = src)
            fixed (byte* dstPtr = dst)
            {
                IntPtr srcP = (IntPtr)(srcPtr + srcOffset);
                IntPtr dstP = (IntPtr)(dstPtr + dstOffset);

			    var dstSize = Options.Ddict == IntPtr.Zero
				    ? ExternMethods.ZSTD_decompressDCtx(dctx, dstP, (size_t)dstLength, srcP, (size_t)srcLength)
				    : ExternMethods.ZSTD_decompress_usingDDict(dctx, dstP, (size_t)dstLength, srcP, (size_t)srcLength, Options.Ddict);

			    dstSize.EnsureZstdSuccess();
                return (int)dstSize;
            }
		}

		public readonly DecompressionOptions Options;

		private IntPtr dctx;
	}
}
