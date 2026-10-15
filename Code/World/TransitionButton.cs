#nullable enable

using Sandbox;

namespace AnakinShardDemo;

/// <summary>
/// A pressable transfer button. Look at it (the HUD shows a prompt) and press the use key (E)
/// to request a transfer to <see cref="TargetShard"/>. Replaces the walk-in TransitionZone.
///
/// Author on a GameObject with a (non-trigger) Collider so the player's press-trace can hit
/// it. The press is routed to the host via <see cref="PlayerTransfer.RequestTransferHost"/> so
/// the gate rules and prepare stay host-authoritative.
/// </summary>
public sealed class TransitionButton : Component, Component.IPressable
{
	[Property] public string TargetShard { get; set; } = "B";
	[Property] public TransferRule Rule { get; set; } = TransferRule.Normal;
	[Property] public int TollAmount { get; set; } = 100;

	/// <summary>Prompt for the button the local client is currently looking at (null if none).
	/// Read by DebugHud to show the popup.</summary>
	public static string? LookPrompt { get; private set; }

	public string Description => Rule switch
	{
		TransferRule.Toll => $"Travel to Shard {TargetShard} - Toll ${TollAmount}",
		TransferRule.Prisoner => $"Prisoner transport to Shard {TargetShard}",
		_ => $"Travel to Shard {TargetShard}",
	};

	public bool CanPress( Component.IPressable.Event e ) => true;

	public bool Press( Component.IPressable.Event e )
	{
		// Runs on the presser's client. Route to the host through the player's PlayerTransfer.
		var agent = e.Source?.GameObject?.Components.GetInAncestorsOrSelf<PlayerTransfer>();
		if ( agent is null )
			return false;

		agent.RequestTransferHost( TargetShard, (int)Rule, TollAmount );
		return true;
	}

	// Look-at popup: set the shared prompt while looking, clear it when looking away.
	public void Hover( Component.IPressable.Event e ) => LookPrompt = $"{Description}   [Press E]";
	public void Blur( Component.IPressable.Event e ) => LookPrompt = null;
}
