namespace Magpie.Core;

public struct QueueFamily {
    public uint? GraphicsFamily { get; set; }
    public uint? PresentFamily { get; set; }
    
    public (uint? Graphics, uint? Present) Family { get; set; }
    
    // public bool IsComplete() {
    //     return Family.Graphics.HasValue && Family.Present.HasValue;
    // }
    
    public bool IsComplete() {
        return GraphicsFamily.HasValue && PresentFamily.HasValue;
    }
}