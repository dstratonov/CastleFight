using UnityEngine;
using System.Collections.Generic;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private int sfxPoolSize = 10;

    private readonly List<AudioSource> sfxPool = new();
    private int sfxPoolIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializeSfxPool();
    }

    private void InitializeSfxPool()
    {
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            sfxPool.Add(source);
        }
    }

    public void PlayMusic(AudioClip clip, float volume = 0.5f, bool loop = true)
    {
        if (musicSource == null || clip == null) return;
        musicSource.clip = clip;
        musicSource.volume = volume;
        musicSource.loop = loop;
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null) musicSource.Stop();
    }

    public void PlaySFX(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        var source = GetNextSfxSource();
        source.clip = clip;
        source.volume = volume;
        source.Play();
    }

    public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    private AudioSource GetNextSfxSource()
    {
        var source = sfxPool[sfxPoolIndex];
        sfxPoolIndex = (sfxPoolIndex + 1) % sfxPool.Count;
        return source;
    }

    public void SetMusicVolume(float volume)
    {
        if (musicSource != null) musicSource.volume = volume;
    }

    public void SetSFXVolume(float volume)
    {
        if (sfxSource != null) sfxSource.volume = volume;
    }
}
