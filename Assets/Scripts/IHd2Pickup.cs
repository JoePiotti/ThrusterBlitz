using UnityEngine;

/// <summary>What the pickup look (Hd2FloatRings) needs to know about any pickup.</summary>
public interface IHd2Pickup
{
    /// <summary>True while the pickup can be taken.</summary>
    bool Ready { get; }

    /// <summary>0 just after it was taken, rising to 1 when it is ready again.</summary>
    float Progress { get; }

    /// <summary>The pickup's color, used for its rings and floor glow.</summary>
    Color LookColor { get; }
}
