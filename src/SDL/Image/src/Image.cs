using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.ImageNativeMethods;

namespace SDLSharp
{
    public static class Image
    {
        public static ImageLoaders Init(ImageLoaders loaders)
        {
            return IMG_Init(loaders);
        }

        public static void Quit()
        {
            IMG_Quit();
        }

        public static unsafe Version RuntimeVersion
        {
            get
            {
                SDL_Version* v = IMG_Linked_Version();
                return new Version(v->major, v->minor, v->patch, 0);
            }
        }

        public static ImageLoaders InitializedLoaders => IMG_Init((ImageLoaders)0);

        public static unsafe Surface Load(RWOps src, string? type = null)
        {
            if (type == null)
            {
                return ErrorIfInvalid(IMG_Load_RW(src, 0));
            }
            else
            {
                Span<byte> buf = stackalloc byte[SL(type)];
                StringToUTF8(type, buf);
                fixed (byte* b = buf)
                    return ErrorIfInvalid(IMG_LoadTyped_RW(src, 0, b));
            }
        }

        public static unsafe Surface Load(string file)
        {
            Span<byte> fbuf = stackalloc byte[SL(file)];
            StringToUTF8(file, fbuf);
            fixed (byte* fb = fbuf)
                return ErrorIfInvalid(IMG_Load(fb));
        }

        public static unsafe Texture LoadTexture(Renderer renderer, string file)
        {
            Span<byte> fbuf = stackalloc byte[SL(file)];
            StringToUTF8(file, fbuf);
            fixed (byte* fb = fbuf)
                return ErrorIfInvalid(IMG_LoadTexture(renderer, fb));
        }

        public static unsafe Texture LoadTexture(Renderer renderer, RWOps src, string? type = null)
        {
            if (type == null)
            {
                return ErrorIfInvalid(IMG_LoadTexture_RW(renderer, src, 0));
            }
            else
            {
                Span<byte> buf = stackalloc byte[SL(type)];
                StringToUTF8(type, buf);
                fixed (byte* b = buf)
                    return ErrorIfInvalid(IMG_LoadTextureTyped_RW(renderer, src, 0, b));
            }
        }

        public static bool IsICO(RWOps ops)
          => IMG_isICO(ops) == 1;
        public static bool IsCUR(RWOps ops)
          => IMG_isCUR(ops) == 1;
        public static bool IsBMP(RWOps ops)
          => IMG_isBMP(ops) == 1;
        public static bool IsGIF(RWOps ops)
          => IMG_isGIF(ops) == 1;
        public static bool IsJPG(RWOps ops)
          => IMG_isJPG(ops) == 1;
        public static bool IsLBM(RWOps ops)
          => IMG_isLBM(ops) == 1;
        public static bool IsPCX(RWOps ops)
          => IMG_isPCX(ops) == 1;
        public static bool IsPNG(RWOps ops)
          => IMG_isPNG(ops) == 1;
        public static bool IsPNM(RWOps ops)
          => IMG_isPNM(ops) == 1;
        public static bool IsSVG(RWOps ops)
          => IMG_isSVG(ops) == 1;
        public static bool IsTIF(RWOps ops)
          => IMG_isTIF(ops) == 1;
        public static bool IsXCF(RWOps ops)
          => IMG_isXCF(ops) == 1;
        public static bool IsXPM(RWOps ops)
          => IMG_isXPM(ops) == 1;
        public static bool IsXV(RWOps ops)
          => IMG_isXV(ops) == 1;
        public static bool IsWEBP(RWOps ops)
          => IMG_isWEBP(ops) == 1;

        public static Surface LoadICO(RWOps ops)
          => ErrorIfInvalid(IMG_LoadICO_RW(ops));
        public static Surface LoadCUR(RWOps ops)
          => ErrorIfInvalid(IMG_LoadCUR_RW(ops));
        public static Surface LoadBMP(RWOps ops)
          => ErrorIfInvalid(IMG_LoadBMP_RW(ops));
        public static Surface LoadGIF(RWOps ops)
          => ErrorIfInvalid(IMG_LoadGIF_RW(ops));
        public static Surface LoadJPG(RWOps ops)
          => ErrorIfInvalid(IMG_LoadJPG_RW(ops));
        public static Surface LoadLBM(RWOps ops)
          => ErrorIfInvalid(IMG_LoadLBM_RW(ops));
        public static Surface LoadPCX(RWOps ops)
          => ErrorIfInvalid(IMG_LoadPCX_RW(ops));
        public static Surface LoadPNG(RWOps ops)
          => ErrorIfInvalid(IMG_LoadPNG_RW(ops));
        public static Surface LoadPNM(RWOps ops)
          => ErrorIfInvalid(IMG_LoadPNM_RW(ops));
        public static Surface LoadSVG(RWOps ops)
          => ErrorIfInvalid(IMG_LoadSVG_RW(ops));
        public static Surface LoadTIF(RWOps ops)
          => ErrorIfInvalid(IMG_LoadTIF_RW(ops));
        public static Surface LoadXCF(RWOps ops)
          => ErrorIfInvalid(IMG_LoadXCF_RW(ops));
        public static Surface LoadXPM(RWOps ops)
          => ErrorIfInvalid(IMG_LoadXPM_RW(ops));
        public static Surface LoadXV(RWOps ops)
          => ErrorIfInvalid(IMG_LoadXV_RW(ops));
        public static Surface LoadWEBP(RWOps ops)
          => ErrorIfInvalid(IMG_LoadWEBP_RW(ops));

        public static unsafe void SavePNG(Surface surface, string file)
        {
            Span<byte> buf = stackalloc byte[SL(file)];
            StringToUTF8(file, buf);
            fixed (byte* b = buf)
                ErrorIfNegative(IMG_SavePNG(surface, b));
        }

        public static void SavePNG(Surface surface, RWOps dst)
        {
            ErrorIfNegative(IMG_SavePNG_RW(surface, dst, 0));
        }

        public static unsafe void SaveJPG(Surface surface, string file, int quality)
        {
            Span<byte> buf = stackalloc byte[SL(file)];
            StringToUTF8(file, buf);
            fixed (byte* b = buf)
                ErrorIfNegative(IMG_SaveJPG(surface, b, quality));
        }

        public static void SaveJPG(Surface surface, RWOps dst, int quality)
        {
            ErrorIfNegative(IMG_SaveJPG_RW(surface, dst, 0, quality));
        }
    }
}
