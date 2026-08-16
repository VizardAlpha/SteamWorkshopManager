using System.Collections.Generic;

namespace SteamWorkshopManager.Models;

public abstract record BbCodeNode;

public sealed record BbCodeText(string Value) : BbCodeNode;

public sealed record BbCodeElement(string Tag, string? Attribute, IReadOnlyList<BbCodeNode> Children) : BbCodeNode;