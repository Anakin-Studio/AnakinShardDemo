using Sandbox;

namespace AnakinShardDemo;

public enum TransferRule
{
	/// <summary>Standard pad: not handcuffed, not in combat, cooldown ok.</summary>
	Normal,
	/// <summary>Like Normal, plus costs <see cref="TransitionZone.TollAmount"/> (deducted once).</summary>
	Toll,
	/// <summary>Jail pad: requires the player to be handcuffed; routes them to Jail (C).</summary>
	Prisoner,
}

/// <summary>
/// A trigger volume that requests a transfer to <see cref="TargetShard"/> when a player walks
/// in. Author it on a GameObject that also has a Collider with IsTrigger = true. Host-only:
/// the gate and prepare all run host-side.
/// </summary>
public sealed class TransitionZone : Component, Component.ITriggerListener
{
	[Property] public string TargetShard { get; set; } = "B";
	[Property] public TransferRule Rule { get; set; } = TransferRule.Normal;
	[Property] public int TollAmount { get; set; } = 100;

	void Component.ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost )
			return;

		// Find the player root from whatever collider entered (feet, body, etc).
		var agent = other.GameObject.Components.GetInAncestorsOrSelf<PlayerTransfer>();
		if ( agent is null )
			return;

		_ = TransferService.Current?.RequestTransfer( agent.GameObject, TargetShard, Rule, TollAmount );
	}
}
