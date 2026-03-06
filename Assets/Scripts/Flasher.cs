using UnityEngine;
using UnityEngine.UI;
using System.Collections;


public class Flasher : MonoBehaviour
{
	public bool useUnscaledTime = true;
	public float initialDelay = 0f;
	public float timeOn = 0.6f;
	public float timeOff = 0.4f;
	[Tooltip("Number of on/off cycles to go before it stops. 0 means go forever.")]
	public int cyclesToFlash = 0;
	public State cycleBeginState = State.Off;
	[Tooltip("After the number of cycles ends, set this state.")]
	public State cycleEndState = State.On;
	[Tooltip("Set to TRUE if the GameObject should be deactivated at cycle end")]
	public bool deactivateAtEnd = false;
	public AudioSource soundOnBegin;
	[Range(0f,1f)] public float volumeOnBegin = 1f;
	public AudioSource soundEventOn;
	[Range(0f,1f)] public float volumeEventOn = 1f;

	public enum State { Off, On }

	Renderer rdr;
	MaskableGraphic uig;
	CanvasGroup cvg;
	WaitForTime waitDelay, waitOn, waitOff;
	Coroutine coDoFlash;


	public float totalDuration =>
		cyclesToFlash <= 0 ? float.PositiveInfinity
		: initialDelay + (timeOn + timeOff) * cyclesToFlash;


	public void Restart()
	{
		if (coDoFlash != null)
		{
			StopCoroutine(coDoFlash);
			SetState(cycleEndState == State.On);
		}
		coDoFlash = StartCoroutine( DoFlash() );
	}


	void Awake()
	{
		rdr = GetComponent<Renderer>();
		uig = GetComponent<MaskableGraphic>();
		cvg = GetComponent<CanvasGroup>();
	}

	void OnEnable()
	{
		waitDelay = new WaitForTime(initialDelay, useUnscaledTime);
		waitOn = new WaitForTime(timeOn, useUnscaledTime);
		waitOff = new WaitForTime(timeOff, useUnscaledTime);

		coDoFlash = StartCoroutine( DoFlash() );
	}


	void OnDisable()
	{
		if (coDoFlash != null)
		{
			StopCoroutine(coDoFlash);
			DoEndState();
		}
	}


	void DoEndState()
	{
		SetState(cycleEndState == State.On);
		if (deactivateAtEnd)
			gameObject.SetActive(false);
		coDoFlash = null;
	}


	IEnumerator DoFlash()
	{
		if (initialDelay > 0)
		{
			SetState(cycleBeginState == State.On);
			waitDelay.Reset();
			yield return waitDelay;
		}

		bool hasBeginSound = soundOnBegin;
		float volume = hasBeginSound ? volumeOnBegin : volumeEventOn;
		AudioSource sound = hasBeginSound ? soundOnBegin : soundEventOn;

		if (cycleBeginState == State.Off)
		{
			for(int count = 0; cyclesToFlash == 0 || count < cyclesToFlash; count++)
			{
				SetState(false);
				waitOff.Reset();
				yield return waitOff;
				SetState(true);
				PlaySoundEvent(sound, volume);
				volume = volumeEventOn;
				sound = soundEventOn;
				waitOn.Reset();
				yield return waitOn;
			}
		}
		else // Begin in the ON state
		{
			for(int count = 0; cyclesToFlash == 0 || count < cyclesToFlash; count++)
			{
				SetState(true);
				PlaySoundEvent(sound, volume);
				volume = volumeEventOn;
				sound = soundEventOn;
				waitOn.Reset();
				yield return waitOn;
				SetState(false);
				waitOff.Reset();
				yield return waitOff;
			}
		}
		DoEndState();
	}


	void SetState( bool isOn )
	{
		if (rdr) rdr.enabled = isOn;
		if (uig) uig.enabled = isOn;
		if (cvg) cvg.alpha = isOn ? 1f : 0f;
	}


	void PlaySoundEvent(AudioSource soundEvent, float volume)
	{
		if (soundEvent)
			soundEvent.PlayOneShot(soundEvent.clip, volume);
	}
}
