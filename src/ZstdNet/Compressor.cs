using System;
using size_t = System.UIntPtr;

namespace ZstdNet
{
	public class Compressor : IDisposable
	{
		public Compressor()
			: this(CompressionOptions.Default)
		{}

		public Compressor(CompressionOptions options)
		{
			Options = options;
			cctx = ExternMethods.ZSTD_createCCtx().EnsureZstdSuccess();

			options.ApplyCompressionParams(cctx);

			if(options.Cdict != IntPtr.Zero)
				ExternMethods.ZSTD_CCtx_refCDict(cctx, options.Cdict).EnsureZstdSuccess();
		}

		~Compressor()
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
			if(cctx == IntPtr.Zero)
				return;

			ExternMethods.ZSTD_freeCCtx(cctx);

			cctx = IntPtr.Zero;
		}

		public byte[] Wrap(byte[] src)
        {
            if (src == null) throw new ArgumentNullException("src");
            return Wrap(src, 0, src.Length);
        }

		public byte[] Wrap(byte[] src, int offset, int length)
		{
			//NOTE: Wrap tries its best, but if src is uncompressible and the size is too large, ZSTD_error_dstSize_tooSmall will be thrown
			var dstCapacity = Math.Min(Consts.MaxByteArrayLength, GetCompressBoundLong((ulong)length));
			var dst = VPB.ByteArrayPool.Rent((int)dstCapacity);

			try
			{
				var dstSize = Wrap(src, offset, length, dst, 0, dst.Length);

				var result = new byte[dstSize];
				Array.Copy(dst, result, dstSize);
				return result;
			}
			finally
			{
				VPB.ByteArrayPool.Return(dst);
			}
		}

		public static int GetCompressBound(int size)
        {
			return (int)ExternMethods.ZSTD_compressBound((size_t)size);
        }

		public static ulong GetCompressBoundLong(ulong size)
        {
			return (ulong)ExternMethods.ZSTD_compressBound((size_t)size);
        }

		public int Wrap(byte[] src, byte[] dst, int offset)
        {
            if (src == null) throw new ArgumentNullException("src");
            return Wrap(src, 0, src.Length, dst, offset, dst.Length - offset);
        }

		public unsafe int Wrap(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
		{
			if(dstOffset < 0 || dstOffset >= dst.Length)
				throw new ArgumentOutOfRangeException("dstOffset");
            if(srcOffset < 0 || srcOffset + srcLength > src.Length)
                throw new ArgumentOutOfRangeException("srcOffset");

            fixed (byte* srcPtr = src)
            fixed (byte* dstPtr = dst)
            {
                IntPtr srcP = (IntPtr)(srcPtr + srcOffset);
                IntPtr dstP = (IntPtr)(dstPtr + dstOffset);

			    var dstSize = Options.Cdict == IntPtr.Zero
					    ? ExternMethods.ZSTD_compressCCtx(cctx, dstP, (size_t)dstLength, srcP, (size_t)srcLength, Options.CompressionLevel)
					    : ExternMethods.ZSTD_compress_usingCDict(cctx, dstP, (size_t)dstLength, srcP, (size_t)srcLength, Options.Cdict);

			    dstSize.EnsureZstdSuccess();
                return (int)dstSize;
            }
		}

		public readonly CompressionOptions Options;

		private IntPtr cctx;
	}
}
