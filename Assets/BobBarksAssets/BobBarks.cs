using System;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;

using RNG = UnityEngine.Random;

public class BobBarks : MonoBehaviour
{
	// Standardized logging
	private static int globalLogID = 0;
	private int thisLogID;
	private bool moduleSolved;

	enum IndicatorStatus {
		Missing,
		Off,
		On,
		SpecialSet1,
		SpecialSet2
	}

	// Guarantees labels can't be seen when faded out
	Color whiteZeroAlpha = new Color(1.0f, 1.0f, 1.0f, 0.0f);

	public KMBombInfo bombInfo;
	public KMAudio bombAudio;
	public KMBombModule bombModule;

	public GameObject[] buttons;
	public TextMesh[] labels;

	// We grab these ourselves since they're a part of the buttons
	private KMSelectable[] buttonCels;
	private Renderer[] buttonRenders;
	private Animator[] buttonAnims;

	public Material MatFlash;
	public Material MatRevert;
	public Material MatSuccess;
	public Material MatFailure;

	private readonly string[] __missAudio = new string[] {
		"miss_a", "miss_b", "miss_c", "miss_d"
	};

	private readonly string[] __goodAudio = new string[] {
		"sound_a", "sound_b", "sound_c", "sound_d"
	};

	private readonly string[] __indicators = new string[] {
		// Vanilla indicators
		"BOB", "CAR", "CLR", "IND", "FRK",
		"FRQ", "MSA", "NSA", "SIG", "SND",
		"TRN",
		// Additional ( > 10 )
		"BUB", "DOG", "ETC", "KEY" 
	};

	private readonly string[] __buttonNames = new string[] {
		"TopLeft", "TopRight", "BottomLeft", "BottomRight"
	};

	IndicatorStatus[] iStatus = new IndicatorStatus[4];
	private int[] assigned = new int[4] { -1, -1, -1, -1 }; // Assigned labels

	private int[] stages = new int[5] { -1, -1, -1, -1, -1 }; // Flashes for each stage
	private int[] correct = new int[5] { -1, -1, -1, -1, -1 }; // Correct inputs
	private int positionInCurrentStage = 0;
	private int currentStage = 0;

	// Labels are hidden before initializing, and after a correct input.
	private bool hideLabels = true;

	private bool soundAllowed;
	private Coroutine showCoroutine;

	// -----
	// State and solution generation
	// -----
	void GenerateSolution()
	{
		int[,] positions;
		int[,] labels;

		// -------
		// Stage 1
		// -------
		positions = new int[,] {
			{0, 1, 2, 3}, // Lit, BOB
			{3, 2, 1, 0}, // Lit
			{1, 0, 3, 2}, // Unlit
			{2, 3, 0, 1}  // Not present or special
		};

		switch(iStatus[stages[0]])
		{
			case IndicatorStatus.On:
				if (assigned[stages[0]] == 0) // It's BOB!
				{
					// Special rule check
					int car = Array.IndexOf(assigned, 1); // Find CAR?
					int key = Array.IndexOf(assigned, 14); // Find KEY?
					if (car != -1 && key != -1)
					{
						for (int i = 0; i < correct.Length; ++i)
							correct[i] = car;
						break;
					}

					// Didn't win a car...
					correct[0] = positions[0, stages[0]];
				}
				else
					correct[0] = positions[1, stages[0]];
				break;
			case IndicatorStatus.Off:
				correct[0] = positions[2, stages[0]];
				break;
			default: // Missing or special
				correct[0] = positions[3, stages[0]];
				break;
		}

		if (correct[4] != -1)
		{
			// Already assigned due to above special rule
			Debug.LogFormat("[Bob Barks #{0}] Stage 1: The special rule applies. Skipping all other stages.", thisLogID);

			Debug.LogFormat("[Bob Barks #{0}] The full sequence of correct presses is: {1}, {2}, {3}, {4}, {5}",
				thisLogID, __buttonNames[correct[0]], __buttonNames[correct[1]], __buttonNames[correct[2]], 
				__buttonNames[correct[3]], __buttonNames[correct[4]]);

			return;
		}

		Debug.LogFormat("[Bob Barks #{0}] Stage 1: Flashing is {1} ({4} {3}), press {2} ({6} {5}).", 
			thisLogID, __buttonNames[stages[0]],  __buttonNames[correct[0]], 
			__indicators[assigned[stages[0]]], iStatus[stages[0]],
			__indicators[assigned[correct[0]]], iStatus[correct[0]]);

		// ----------------
		// All Other Stages
		// ----------------
		labels = new int[,] {
		//   BOB CAR CLR IND FRK FRQ MSA NSA SIG SND TRN
			{  1,  2,  8,  9,  5,  4,  7,  6, 10,  3,  0, -1}, // ... present and lit
			{  9,  7,  5,  8,  6, 10,  0,  3,  1,  4,  2, -1}, // ... present, but not lit
			{  4,  3,  9,  2, 10,  8,  5,  1,  7,  0,  6, -1}, // ... is SpecialSet1
			{ 10,  0,  4,  1,  8,  3,  2,  5,  6,  9,  7, -1}, // ... is SpecialSet2
			{  5,  8,  0,  4,  2,  6, 10,  9,  3,  7,  1, -1}, // ... is absent, but flashing present
			{  2,  5,  6,  0,  7,  9,  3,  8,  4,  1, 10, -1}  // ... is absent, flashing also absent
		};

		positions = new int[,] {
		//   BOB CAR CLR IND FRK FRQ MSA NSA SIG SND TRN SpS
			{  2,  1,  3, -2,  0,  0, -3,  3,  2,  1, -1, -4}, // ... present and lit
			{  0,  2, -3,  1,  3,  1,  3,  0, -1, -4,  2, -2}, // ... present, but not lit
			{ -4,  0,  3, -1,  1,  3,  2, -3,  1,  2,  0, -3}, // ... is SpecialSet1
			{ -2, -1,  0,  3, -3,  2,  0,  2,  0,  3,  1, -1}, // ... is SpecialSet2
			{  3,  1,  2,  0,  2, -3,  3,  0,  1, -1, -2,  0}, // ... is absent, but flashing present
			{  1,  3, -1,  2, -4,  1,  2,  3, -3,  0,  3, -4}  // ... is absent, flashing also absent
		};

		int[,] buttonMovements = new int[,] {
			{0, 1, 2, 3}, // -4 Flashing
			{2, 0, 3, 1}, // -3 Counterclockwise
			{3, 2, 1, 0}, // -2 Opposing
			{1, 3, 0, 2}  // -1 Clockwise
		};

		for (int i = 1; i < 5; ++i)
		{
			int column = (assigned[stages[i]] > 10) ? 11 : assigned[stages[i]];
			int row;

			switch(iStatus[correct[i - 1]])
			{
				case IndicatorStatus.On:
					row = 0;
					break;
				case IndicatorStatus.Off:
					row = 1;
					break;
				case IndicatorStatus.SpecialSet1:
					row = 2;
					break;
				case IndicatorStatus.SpecialSet2:
					row = 3;
					break;
				default: // Missing
					row = (iStatus[stages[i]] == IndicatorStatus.On || iStatus[stages[i]] == IndicatorStatus.Off)
						? 4 : 5;
					break;
			}

			int targetLabel = Array.IndexOf(assigned, labels[row, column]);
			correct[i] = (targetLabel == -1) ? positions[row, column] : targetLabel;

			// ===
			// This entire section is ALL logging, and can be removed if needed.
			Debug.LogFormat("[Bob Barks #{0}] Stage {1}: Previous: {5} {4}, Flashing: {7} {6} ... reading from ({2}, {3})", 
				thisLogID, i + 1, row, column, __indicators[assigned[correct[i - 1]]], iStatus[correct[i - 1]],
				__indicators[assigned[stages[i]]], iStatus[stages[i]]);

			if (targetLabel != -1)
			{
				Debug.LogFormat("[Bob Barks #{0}] Stage {1}: Label requested, {2}, is present on module in location {3}. Press it.",
					thisLogID, i + 1, __indicators[labels[row, column]], __buttonNames[targetLabel]);
			}
			else if (labels[row, column] == -1)
			{
				Debug.LogFormat("[Bob Barks #{0}] Stage {1}: This cell has no label listed.", 
					thisLogID, i + 1);
			}
			else
			{
				Debug.LogFormat("[Bob Barks #{0}] Stage {1}: Label requested, {2}, is not present on module.",
					thisLogID, i + 1, __indicators[labels[row, column]]);
			}

			if (targetLabel == -1 && correct[i] < 0)
			{
				int translation = buttonMovements[4 + correct[i], stages[i]];
				string translationName;
				switch (correct[i])
				{
					case -4: translationName = "flashing"; break;
					case -3: translationName = "counter-clockwise"; break;
					case -2: translationName = "opposing"; break;
					default: translationName = "clockwise"; break;
				}
				Debug.LogFormat("[Bob Barks #{0}] Stage {1}: Requesting {4} translation. {2} is flashing, so press {3}.",
					thisLogID, i + 1, __buttonNames[stages[i]], __buttonNames[translation], translationName);
			}
			else if (targetLabel == -1)
			{
				Debug.LogFormat("[Bob Barks #{0}] Stage {1}: Button requested is {2}.",
					thisLogID, i + 1, __buttonNames[correct[i]]);
			}
			// End logging section
			// ===

			if (correct[i] < 0)
				correct[i] = buttonMovements[4 + correct[i], stages[i]];
		}

		Debug.LogFormat("[Bob Barks #{0}] The full sequence of correct presses is: {1}, {2}, {3}, {4}, {5}",
			thisLogID, __buttonNames[correct[0]], __buttonNames[correct[1]], __buttonNames[correct[2]], 
			__buttonNames[correct[3]], __buttonNames[correct[4]]);
	}

	void Randomize()
	{
		int i;
		IndicatorStatus[] allStatuses = new IndicatorStatus[15];
		
		// Start with a list, add values representing present indicators or
		// non-vanilla indicators more than once to weight results.
		// Non-present indicators get a weight of 2
		// Present indicators get a weight of 8 (highly likely to show)
		// Non-vanilla indicators get a weight of 3
		List<int> weights = new List<int>();
		for (i = 0; i <= 10; ++i)
		{
			weights.AddRange(Enumerable.Repeat(i, 2));
			if (bombInfo.IsIndicatorPresent(__indicators[i]))
			{
				allStatuses[i] = (bombInfo.IsIndicatorOn(__indicators[i]))
					? IndicatorStatus.On : IndicatorStatus.Off;
				weights.AddRange(Enumerable.Repeat(i, 6)); // Add six more copies of it (eight total)
			}
		}
		for (; i < __indicators.Length; ++i)
		{
			allStatuses[i] = (i > 12)
				? IndicatorStatus.SpecialSet2 : IndicatorStatus.SpecialSet1;
			weights.AddRange(Enumerable.Repeat(i, 3));
		}

		// Now randomly pick from our weighted list to find our four labels
		for (i = 0; i < assigned.Length; ++i)
		{
			assigned[i] = weights[RNG.Range(0, weights.Count)];
			weights.RemoveAll(v => v == assigned[i]);

			// For solution generation
			iStatus[i] = allStatuses[assigned[i]];
		}

		for (i = 0; i < labels.Length; ++i)
		{
			// Set labels to match what the buttons have been assigned to
			labels[i].text = __indicators[assigned[i]];
		}
		Debug.LogFormat("[Bob Barks #{0}] Buttons in reading order: {1}, {2}, {3}, {4}",
			thisLogID, labels[0].text, labels[1].text, labels[2].text, labels[3].text);

		// Again use a list, but to add a little weight and balancing to flashing buttons
		// instead of using pure randomness
		weights = new List<int>() { 0, 0, 1, 1, 2, 2, 3, 3 };
		for (i = 0; i < stages.Length; ++i)
		{
			int rpos = RNG.Range(0, weights.Count);
			stages[i] = weights[rpos];
			weights.RemoveAt(rpos);
		}

		Debug.LogFormat("[Bob Barks #{0}] Buttons will flash in this sequence: {1}, {2}, {3}, {4}, {5}",
			thisLogID, __buttonNames[stages[0]], __buttonNames[stages[1]], __buttonNames[stages[2]], 
			__buttonNames[stages[3]], __buttonNames[stages[4]]);
	}

	// -----
	// Visual effects
	// -----
	private IEnumerator SolveAnimation()
	{
		hideLabels = true; // Just in case
		yield return new WaitForSeconds(0.35f);

		buttonRenders[0].material = MatRevert;
		buttonRenders[1].material = MatRevert;
		buttonRenders[2].material = MatRevert;
		buttonRenders[3].material = MatRevert;
		yield return new WaitForSeconds(0.1f);

		buttonRenders[3].material = MatSuccess;
		bombAudio.PlaySoundAtTransform(__goodAudio[3], gameObject.transform);
		yield return new WaitForSeconds(0.125f);

		buttonRenders[1].material = MatSuccess;
		bombAudio.PlaySoundAtTransform(__goodAudio[1], gameObject.transform);
		yield return new WaitForSeconds(0.225f);

		buttonRenders[0].material = MatSuccess;
		bombAudio.PlaySoundAtTransform(__goodAudio[0], gameObject.transform);
		yield return new WaitForSeconds(0.125f);

		buttonRenders[2].material = MatSuccess;
		bombAudio.PlaySoundAtTransform("sound_c_fin", gameObject.transform);
		bombModule.HandlePass();
	}

	private IEnumerator ShowSequence(float initialShowDelay)
	{
		int displayStage = 0;

		// Note: We do this because handling the pressed button's material wasn't our job previously, but is now.
		initialShowDelay -= 0.5f;
		yield return new WaitForSeconds(0.5f);

		buttonRenders[0].material = MatRevert;
		buttonRenders[1].material = MatRevert;
		buttonRenders[2].material = MatRevert;
		buttonRenders[3].material = MatRevert;
		yield return new WaitForSeconds(initialShowDelay);

		while (!moduleSolved)
		{
			if (soundAllowed)
				bombAudio.PlaySoundAtTransform(__goodAudio[stages[displayStage]], gameObject.transform);

			buttonRenders[stages[displayStage]].material = MatFlash;
			yield return new WaitForSeconds(0.5f);

			buttonRenders[stages[displayStage]].material = MatRevert;

			if (++displayStage > currentStage)
			{
				displayStage = 0;
				yield return new WaitForSeconds(2.25f);
			}
			else
				yield return new WaitForSeconds(0.25f);
		}
	}

	private IEnumerator LabelShowHide()
	{
		int i;
		int curStep = 12, targetStep = 12;
		bool lastHideLabels = hideLabels;

		while (!moduleSolved)
		{
			// Until we notice a change, wait.
			while (lastHideLabels == hideLabels)
				yield return new WaitForSeconds(0.1f);

			targetStep = (hideLabels) ? 12 : 0;
			while (curStep != targetStep)
			{
				curStep += (curStep < targetStep) ? 1 : -1;

				Color objColor = Color.Lerp(Color.white, whiteZeroAlpha, (float)curStep / 12.0f);
				for (i = 0; i < labels.Length; ++i)
					labels[i].color = objColor;

				yield return new WaitForSeconds(0.025f);

				// Reset in case we're in the middle of animating and another shift comes in
				targetStep = (hideLabels) ? 12 : 0;
			}

			lastHideLabels = hideLabels;
		}

		// Guarantee zero alpha when solved
		for (i = 0; i < labels.Length; ++i)
			labels[i].color = whiteZeroAlpha;
	}

	// -----
	// Interactions, etc
	// -----
	void ButtonInteract(int pressed)
	{
		buttonCels[pressed].AddInteractionPunch(0.5f);
		buttonAnims[pressed].Play("ButtonPressed", 0, 0);

		if (moduleSolved)
		{
			bombAudio.PlaySoundAtTransform(__goodAudio[pressed], gameObject.transform);
			return;
		}

		soundAllowed = true;

		if (correct[0] == -1)
		{
			// Bomb isn't even fully loaded yet.
			Debug.LogFormat("[Bob Barks #{0}] STRIKE: You pressed {1} when I expected you to wait for the bomb to activate. Have some patience, sheesh.",
				thisLogID, __buttonNames[pressed]);
			bombAudio.PlaySoundAtTransform(__missAudio[pressed], gameObject.transform);
			bombModule.HandleStrike();
			return;
		}

		StopCoroutine(showCoroutine);
		buttonRenders[0].material = MatRevert;
		buttonRenders[1].material = MatRevert;
		buttonRenders[2].material = MatRevert;
		buttonRenders[3].material = MatRevert;

		if (correct[positionInCurrentStage] == pressed)
		{
			bombAudio.PlaySoundAtTransform(__goodAudio[pressed], gameObject.transform);
			buttonRenders[pressed].material = MatFlash;

			if (++positionInCurrentStage > currentStage)
			{
				Debug.LogFormat("[Bob Barks #{0}] Correct input for stage {1}.",
					thisLogID, currentStage + 1);

				if (++currentStage > 4)
				{
					Debug.LogFormat("[Bob Barks #{0}] SOLVE: All stages passed.", thisLogID);
					moduleSolved = true;
					showCoroutine = StartCoroutine(SolveAnimation());
					return;
				}
				positionInCurrentStage = 0;
				showCoroutine = StartCoroutine(ShowSequence(1.5f));
			}
			else
			{
				// Wait much longer if in the middle of input
				showCoroutine = StartCoroutine(ShowSequence(5.0f));
			}

			// Start hiding labels on any correct input.
			hideLabels = true;
		}
		else
		{
			Debug.LogFormat("[Bob Barks #{0}] STRIKE: You pressed {1} when I expected {2}. Resetting to beginning of stage {3}.",
				thisLogID, __buttonNames[pressed], __buttonNames[correct[positionInCurrentStage]], currentStage + 1);

			bombAudio.PlaySoundAtTransform(__missAudio[pressed], gameObject.transform);
			bombModule.HandleStrike();
			positionInCurrentStage = 0;

			showCoroutine = StartCoroutine(ShowSequence(1.5f));
			buttonRenders[pressed].material = MatFailure;

			// Show the labels again on strike, so you can see where you went wrong.
			hideLabels = false;
		}
	}

	void Awake()
	{
		thisLogID = ++globalLogID;

		// Assign renders and animators now.
		buttonCels = new KMSelectable[buttons.Length];
		buttonRenders = new Renderer[buttons.Length];
		buttonAnims = new Animator[buttons.Length];
		for (int i = 0; i < buttons.Length; ++i)
		{
			buttonCels[i] = buttons[i].GetComponentInChildren<KMSelectable>();
			buttonRenders[i] = buttons[i].GetComponentInChildren<Renderer>();
			buttonAnims[i] = buttons[i].GetComponent<Animator>();
		}

		for (int tmp_i = 0; tmp_i < buttonCels.Length; ++tmp_i)
		{
			int i = tmp_i;
			buttonCels[tmp_i].OnInteract += delegate() {
				ButtonInteract(i); 
				return false;
			};
		}

		// Hide labels by default.
		for (int i = 0; i < labels.Length; ++i)
			labels[i].color = whiteZeroAlpha;

		// Handles label fading in/out for the duration of the bomb.
		StartCoroutine(LabelShowHide());

		bombModule.OnActivate += delegate()
		{
			Randomize();
			GenerateSolution();

			showCoroutine = StartCoroutine(ShowSequence(1.0f));
			hideLabels = false;
		};
	}

	// -----
	// Twitch Plays Support
	// -----
#pragma warning disable 414
	private readonly string TwitchManualCode = "https://ktane.qute.dog/manuals/Bob%20Barks.html";
	private readonly string TwitchHelpMessage = @"Use '!{0} TL TR BL BR' (position) or '!{0} 1 2 3 4' (reading order) to press the buttons. Use '!{0} shut up' to silence until another input is given. -- NOTE: This module is unfinished, but all functionality is present.";
#pragma warning restore 414

	public IEnumerator ProcessTwitchCommand(string command)
	{
		// TP only silence command
		if (Regex.IsMatch(command, @"^\s*(silence|shut.*up|be\s*quiet)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
		{
			Debug.LogFormat("[Bob Barks #{0}] Received Twitch Plays command to silence SFX.", thisLogID);

			yield return null;
			soundAllowed = false;
			yield break;
		}

		List<string> cmds = command.Split(' ').ToList();
		List<KMSelectable> presses = new List<KMSelectable>();
		if (cmds.Count > 16)
		{
			// There's no concievable reason to have more than 16 chained commands:
			// one ignored "press" or "select", and 15 button presses to solve from stage 1.
			yield break; // error
		}
		for (int i = 0; i < cmds.Count; ++i)
		{
			if (Regex.IsMatch(cmds[i], @"^(press|select)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			{
				if (i == 0)
					continue; // Ignore filler press/select at the start
				yield break; // error
			}
			if (Regex.IsMatch(cmds[i], @"^(TL|1)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
				presses.Add(buttonCels[0]);
			else if (Regex.IsMatch(cmds[i], @"^(TR|2)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
				presses.Add(buttonCels[1]);
			else if (Regex.IsMatch(cmds[i], @"^(BL|3)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
				presses.Add(buttonCels[2]);
			else if (Regex.IsMatch(cmds[i], @"^(BR|4)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
				presses.Add(buttonCels[3]);
			else
				yield break; // error
		}
		if (presses.Count > 0)
		{
			yield return null;
			KMSelectable[] pressArray = presses.ToArray();
			Debug.LogFormat("[Bob Barks #{0}] Received Twitch Plays command to press {1} buttons.", thisLogID, pressArray.Length);
			yield return pressArray;

			// If module is in solve animation after pressing, then return "solve" so the solver gets proper credit
			if (moduleSolved)
			{
				Debug.LogFormat("[Bob Barks #{0}] Yielding solve to the TP handler because the solving animation is playing.", thisLogID);
				yield return "solve";
			}
		}
		yield break;
	}

	void TwitchHandleForcedSolve()
	{
		if (moduleSolved)
			return;

		Debug.LogFormat("[Bob Barks #{0}] SOLVE: Force solve requested by Twitch Plays.", thisLogID);
		moduleSolved = true;
		StopCoroutine(showCoroutine);
		showCoroutine = StartCoroutine(SolveAnimation());
	}
}
