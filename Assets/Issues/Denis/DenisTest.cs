using PurrNet;
using PurrNet.Logging;
using UnityEngine;

public class DenisTest : NetworkIdentity
{
    [SerializeField] private GameObject _prefab;

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
            return;

        var obj = Instantiate(_prefab);
        obj.transform.parent = transform;
        obj.transform.localPosition = new Vector3(0, 1, 5);
        obj.GetComponent<NetworkIdentity>().GiveOwnership(localPlayer);
    }
}
