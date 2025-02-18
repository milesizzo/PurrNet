using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    public static Dictionary<PlayerID, PlayerController> Players { get; } = new();

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        base.OnOwnerChanged(oldOwner, newOwner, asServer);

        if (oldOwner != null && !asServer)
        {
            Players.Remove(oldOwner.Value);
        }

        if (newOwner != null && !asServer)
        {
            Debug.Log($"Player {newOwner} joined the game.");
            Players[newOwner.Value] = this;
        }
    }

    public static PlayerController Get(PlayerID playerID)
    {
        return Players.GetValueOrDefault(playerID);
    }
}
