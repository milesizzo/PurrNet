using System;
using PurrNet;
using PurrNet.Logging;
using UnityEngine;
using Random = UnityEngine.Random;

public class MoveCube : NetworkBehaviour
{
    [SerializeField] private float _speed = 1f;
    [SerializeField] private float _xRange = 5f;
    [SerializeField] private float _yRange = 5f;
    
    [SerializeField] SyncVar<bool> _isAlive = new SyncVar<bool>();
    
    float _noiseOffset;

    private void Awake()
    {
        _noiseOffset = Random.value * 100f;
    }

    protected override void OnSpawned()
    {
        this.enabled = isController;
        
        if (_isAlive.isControllingSyncVar)
            _isAlive.value = true;
    }
    
    [ObserversRpc]
    private void SayGoodbye()
    {
        PurrLogger.Log("Goodbye!");
    }

    protected override void OnDespawned()
    {
        if (_isAlive.isControllingSyncVar)
            _isAlive.value = false;
        
        SayGoodbye();
        PurrLogger.Log("Despawned, _isAlive.value = " + _isAlive.value);
    }

    protected override void OnDespawned(bool asServer)
    {
        PurrLogger.Log("Despawned as " + (asServer ? "server" : "client"));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isController)
            return;
        transform.SetParent(other.transform);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!isController)
            return;
        if (transform.parent == other.transform)
            transform.SetParent(null);
    }

    void Update()
    {
        var xNoise = Mathf.PerlinNoise(Time.time * _speed + _noiseOffset, _noiseOffset) * _xRange - _xRange / 2f;
        var yNoise = Mathf.PerlinNoise(_noiseOffset, Time.time * _speed + _noiseOffset) * _yRange - _yRange / 2f;
        transform.position = new Vector3(xNoise, yNoise, 0f);
    }
}
