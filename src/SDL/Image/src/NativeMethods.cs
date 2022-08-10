using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    static unsafe class ImageNativeMethods
    {

        [DllImport("SDL2_image")]
        public static extern /*const*/ SDL_Version* IMG_Linked_Version();

        [DllImport("SDL2_image")]
        public static extern ImageLoaders IMG_Init(ImageLoaders loaders);

        [DllImport("SDL2_image")]
        public static extern void IMG_Quit();

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadTyped_RW(RWOps src, int freesrc, /*const char*/ byte* type);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_Load(/*const char*/ byte* file);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_Load_RW(RWOps src, int freesrc);

        [DllImport("SDL2_image")]
        public static extern Texture IMG_LoadTexture(Renderer renderer, /*const char*/ byte* file);

        [DllImport("SDL2_image")]
        public static extern Texture IMG_LoadTexture_RW(Renderer renderer, RWOps src, int freesrc);

        [DllImport("SDL2_image")]
        public static extern Texture IMG_LoadTextureTyped_RW(Renderer renderer, RWOps src, int freesrc, /*const char*/ byte* type);

        [DllImport("SDL2_image")]
        public static extern int IMG_isICO(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isCUR(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isBMP(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isGIF(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isJPG(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isLBM(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isPCX(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isPNG(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isPNM(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isSVG(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isTIF(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isXCF(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isXPM(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isXV(RWOps src);

        [DllImport("SDL2_image")]
        public static extern int IMG_isWEBP(RWOps src);


        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadICO_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadCUR_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadBMP_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadGIF_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadJPG_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadLBM_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadPCX_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadPNG_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadPNM_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadSVG_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadTIF_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadXCF_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadXPM_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadXV_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_LoadWEBP_RW(RWOps src);

        [DllImport("SDL2_image")]
        public static extern Surface IMG_ReadXPMFromArray(/*char*/ byte** xpm);


        [DllImport("SDL2_image")]
        public static extern int IMG_SavePNG(Surface surface, /*cons char*/ byte* file);

        [DllImport("SDL2_image")]
        public static extern int IMG_SaveJPG(Surface surface, /*cons char*/ byte* file, int quality);

        [DllImport("SDL2_image")]
        public static extern int IMG_SavePNG_RW(Surface surface, RWOps dst, int freedst);

        [DllImport("SDL2_image")]
        public static extern int IMG_SaveJPG_RW(Surface surface, RWOps dst, int freedst, int quality);
    }

    public enum ImageLoaders
    {
        JPG = 1,
        PNG = 2,
        TIF = 4,
        WebP = 8,
    }
}
