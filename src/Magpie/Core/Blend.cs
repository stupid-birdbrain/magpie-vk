namespace Magpie.Core;

public enum BlendOperation : byte {
    Add,
    Subtract,
    ReverseSubtract,
    Min,
    Max
}

public enum BlendFactor : byte {
    Zero,
    One,
    SourceColor,
    OneMinusSourceColor,
    DestinationColor,
    OneMinusDestinationColor,
    SourceAlpha,
    OneMinusSourceAlpha,
    DestinationAlpha,
    OneMinusDestinationAlpha,
    ConstantColor,
    OneMinusConstantColor,
    ConstantAlpha,
    OneMinusConstantAlpha,
    SourceAlphaSaturate,
    SourceOneColor,
    OneMinusSourceOneColor,
    SourceOneAlpha,
    OneMinusSourceOneAlpha
}

public struct BlendSettings(
    bool blendEnable,
    BlendFactor sourceColorBlend,
    BlendFactor destinationColorBlend,
    BlendOperation colorBlendOperation,
    BlendFactor sourceAlphaBlend,
    BlendFactor destinationAlphaBlend,
    BlendOperation alphaBlendOperation) {
        
    public static readonly BlendSettings Opaque = new(false, BlendFactor.One, BlendFactor.Zero, BlendOperation.Add, BlendFactor.One, BlendFactor.Zero, BlendOperation.Add);
    public static readonly BlendSettings AlphaBlend = new(true, BlendFactor.One, BlendFactor.OneMinusSourceAlpha, BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSourceAlpha, BlendOperation.Add);
    public static readonly BlendSettings Additive = new(true, BlendFactor.SourceAlpha, BlendFactor.One, BlendOperation.Add, BlendFactor.SourceAlpha, BlendFactor.One, BlendOperation.Add);
    public static readonly BlendSettings NonPremultiplied = new(true, BlendFactor.SourceAlpha, BlendFactor.OneMinusSourceAlpha, BlendOperation.Add, BlendFactor.SourceAlpha, BlendFactor.OneMinusSourceAlpha, BlendOperation.Add);

    public bool BlendEnable = blendEnable;
    public BlendFactor SourceColorBlend = sourceColorBlend;
    public BlendFactor DestinationColorBlend = destinationColorBlend;
    public BlendOperation ColorBlendOperation = colorBlendOperation;
    public BlendFactor SourceAlphaBlend = sourceAlphaBlend;
    public BlendFactor DestinationAlphaBlend = destinationAlphaBlend;
    public BlendOperation AlphaBlendOperation = alphaBlendOperation;
}