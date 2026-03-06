using UnityEngine;
using System.Collections;


public class WaitForTime : IEnumerator
{
	bool useUnscaledTime;
	float duration;
	float timeEnd;

	public WaitForTime(float duration, bool useUnscaledTime)
	{
		this.useUnscaledTime = useUnscaledTime;
		this.duration = duration;
		Reset();
	}
	public object Current { get { return null; } }
	public void Reset() { timeEnd = useUnscaledTime ? Time.unscaledTime + duration : Time.time + duration; }
	public bool MoveNext() { return (useUnscaledTime ? Time.unscaledTime : Time.time) < timeEnd; }
}