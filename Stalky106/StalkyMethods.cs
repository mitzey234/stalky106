﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.Loader;
using UnityEngine;

using Random = UnityEngine.Random;

namespace Stalky106
{
	public class StalkyMethods
	{
		// Times
		public float disableFor;
		public float stalky106LastTime;
		private float stalkyAvailable;

		public float StalkyCooldown
		{
			set
			{
				stalkyAvailable = Time.time + value;
				if (plugin.Config.Preferences.AnnounceReady)
				{
					plugin.AddCoroutine(MEC.Timing.CallDelayed(value, () => plugin.NewCoroutine(AnnounceGlobalCooldown())));
				}
			}
			get => stalkyAvailable - Time.time;
		}
		IEnumerator<float> AnnounceGlobalCooldown()
		{
			while (stalkyAvailable > Time.time)
			{
				yield return MEC.Timing.WaitForSeconds(0.15F);
			}
			if (!plugin.Config.IsEnabled || !plugin.Config.Preferences.AnnounceReady) yield break;

			foreach (Player player in Player.List)
			{
				if (player.Role == RoleType.Scp106)
				{
					player.Broadcast(6, plugin.Config.Translations.StalkReady);
				}
			}
		}

		public readonly string[] defaultRoleNames = new string[]
		  { "<color=#F00>SCP-173</color>", "<color=#FF8E00>Class D</color>", "Spectator",
			"<color=#F00>SCP-106</color>", "<color=#0096FF>NTF Scientist</color>", "<color=#F00>SCP-049</color>",
			"<color=#FFFF7CFF>Scientist</color>", "SCP-079", "<color=#008f1e>Chaos Insurgent</color>",
			"<color=#f00>SCP-096</color>", "<color=#f00>Zombie</color>",
			"<color=#0096FF>NTF Lieutenant</color>", "<color=#0096FF>NTF Commander</color>",
			"<color=#0096FF>NTF Cadet</color>",
			"Tutorial", "<color=#59636f>Facility Guard</color>",
			"<color=#f00>SCP-939-53</color>", "<color=#f00>SCP-939-89</color>" };

		// It will ALWAYS ignore spectators and unconnected players.
		private static readonly RoleType[] alwaysIgnore = new RoleType[] { RoleType.None, RoleType.Spectator, RoleType.Scp079 };
		private readonly StalkyPlugin plugin;
		public StalkyMethods(StalkyPlugin plugin)
		{
			this.plugin = plugin;
		}
		internal bool Stalk(Player player)
		{
			// If Stalky is disabled by force, don't even create a portal for the guy
			// Avoids 1-frame trick to (probably unintentionally) "cancel" the Stalk.
			if (disableFor > Time.time)
			{
				return false;
			}

			float timeDifference = Time.time - stalky106LastTime;
			float cdAux = StalkyCooldown;
			if (timeDifference > 6f)
			{
				stalky106LastTime = Time.time;
				if (cdAux < 0)
				{
					player.ClearBroadcasts();
					player.Broadcast(6, plugin.Config.Translations.DoubleClick);
				}
				return true;
			}
			else
			{
				player.ClearBroadcasts();
				if (cdAux > 0)
				{
					stalky106LastTime = Time.time;
					int i = 0;
					for (; i < 5 && cdAux > i; i++) player.Broadcast(1, plugin.Config.Translations.Cooldown_Message.Replace("$time", (cdAux - i).ToString("00")));
					disableFor = Time.time + i + 1;
					return true;
				}
				disableFor = Time.time + 4;
				plugin.NewCoroutine(StalkCoroutine(player));
				return false;
			}
		}
		// Wrapper for SCP-035
		public IEnumerator<float> StalkCoroutine(Player player)
		{
			List<Player> list = new List<Player>();
			Scp106PlayerScript scp106Script = player.GameObject.GetComponent<Scp106PlayerScript>();

			// We can't (or shouldn't) do it in one iteration, as we "need" to pick a random one.
			// If we did do it in one iteration, we would have to "go back" at some point,
			// meaning we aren't really saving any CPU time at all.

			List<Player> scp035s = null;
			foreach (var plugin in Loader.Plugins)
			{
				if (plugin.Name == "scp035")
				{
					try
					{
						scp035s = (List<Player>)Loader.Plugins.First(pl => pl.Name == "scp035").Assembly.GetType("scp035.API.Scp035Data").GetMethod("GetScp035s", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
					}
					catch (Exception e)
					{
						Log.Debug("Failed getting 035s: " + e);
						scp035s = new List<Player>();
					}
					break;
				}
				else
				{
					scp035s = new List<Player>();
				}
			}

			foreach (Player plausibleTarget in Player.List)
			{
				if (scp035s.Contains(plausibleTarget)) continue;
				if (!alwaysIgnore.Contains(plausibleTarget.Role)
					&& !plugin.Config.Preferences.IgnoreRoles.Contains(plausibleTarget.Role)
					&& !plugin.Config.Preferences.IgnoreTeams.Contains(plausibleTarget.Team))
				{
					if (plugin.Config.Preferences.SameZoneOnly)
					{
						if (plausibleTarget.CurrentRoom.Zone == player.CurrentRoom.Zone
							|| plausibleTarget.CurrentRoom.Zone == Exiled.API.Enums.ZoneType.Unspecified)
						{
							list.Add(plausibleTarget);
						}
					}
					else
					{
						list.Add(plausibleTarget);
					}
				}
			}
			if (list.IsEmpty())
			{
				player.Broadcast(4, plugin.Config.Translations.NoTargetsLeft);
				yield break;
			}

			// Wait one frame after computing the players
			yield return MEC.Timing.WaitForOneFrame;

			Player target = this.FindTarget(list, scp106Script.teleportPlacementMask, out Vector3 portalPosition);

			if (target == default || (Vector3.Distance(portalPosition, StalkyPlugin.pocketDimension) < 40f))
			{
				player.Broadcast(4, plugin.Config.Translations.NoTargetsLeft);
				yield break;
			}
			if (portalPosition.Equals(Vector3.zero))
			{
				player.Broadcast(4, plugin.Config.Translations.Error);
				yield break;
			}

			// Wait another frame after the while loops that goes over players.
			// Only useful for +100 player servers and the potatest server in this case, 
			// but it might help with poorly implemented logging systems. And bruh it's 2 frames.
			yield return MEC.Timing.WaitForOneFrame;

			plugin.NewCoroutine(PortalProcedure(scp106Script, portalPosition - Vector3.up));

			StalkyCooldown = plugin.Config.Preferences.Cooldown;
			stalky106LastTime = Time.time;
			disableFor = Time.time + 10f;
			if (!plugin.Config.Translations.RoleDisplayNames.TryGetValue(target.Role, out string className))
			{
				className = defaultRoleNames[(int)target.Role];
			}

			;
			player.Broadcast(6, ReplaceAfterToken(plugin.Config.Translations.StalkMessage, '$',
									new Tuple<string, object>[] {
										new Tuple<string, object>("player", target.Nickname),
										new Tuple<string, object>("class", className),
										new Tuple<string, object>("cd", plugin.Config.Preferences.Cooldown)}));
		}

		private Player FindTarget(List<Player> validPlayerList, LayerMask teleportPlacementMask, out Vector3 portalPosition)
		{
			Player target;
			stalky106LastTime = Time.time;

			do
			{
				int index = Random.Range(0, validPlayerList.Count);
				target = validPlayerList[index];
				Physics.Raycast(new Ray(target.GameObject.transform.position, -Vector3.up), out RaycastHit raycastHit, 10f, teleportPlacementMask);

				// If the raycast fails, the point will be (0, 0, 0), basically Vector3.zero
				portalPosition = raycastHit.point;
				validPlayerList.RemoveAt(index);
			} while ((portalPosition.Equals(Vector3.zero) || Vector3.Distance(portalPosition, StalkyPlugin.pocketDimension) < 40f) && validPlayerList.Count > 0);

			return target;
		}

		// Fully commented to show what it does step by step
		public IEnumerator<float> PortalProcedure(Scp106PlayerScript script, Vector3 pos)
		{
			// Sets the NetworkportalPosition to pos. Since it's a C# Property,
			// NetworkPortalPosition is a "set" method and calls some behaviour on its own.
			// Using provided API functions ensures reliability in future updates.
			script.NetworkportalPosition = pos;

			// Checks the config auto_tp to teleport SCP-106
			if (plugin.Config.Preferences.AutoTp)
			{
				yield return MEC.Timing.WaitForSeconds(plugin.Config.Preferences.AutoDelay);

				// Do-While prevents you from avoiding the auto-tp by jumping.
				// Bug: frame-perfect jumps will move SCP-106 on the server, but the client
				// will be able to move. Hence, why the config: force_auto_tp.

				// This is due to the fact CallCmdUsePortal will try to move SCP-106
				// and tell the client "hey, I'm moving you, stop right there."
				// Since the client is airbound, the client won't stop moving but will
				// do the SCP-106 portal animation.
				do
				{
					script.UserCode_CmdUsePortal(); // "Tells" the player he's teleporting, for some reason CmdUsePortal doesn't work anymore
					yield return MEC.Timing.WaitForOneFrame; // Wait for one frame to tell him again
				}
				while (!script.goingViaThePortal // Stops teleporting the player if SCP-106 is already going through the portal
				&& plugin.Config.Preferences.ForceAutoTp); // If force_auto_tp, the do-while will only execute once.
			}
		}
		private static readonly StringBuilder builder = new StringBuilder();
		/// <summary>
		/// Optimized method that replaces a <see cref="string"/> based on an <see cref="Tuple[]"/>
		/// </summary>
		/// <param name="source">The string to use as source</param>
		/// <param name="token">The starting token</param>
		/// <param name="valuePairs">The value pairs (tuples) to use as "key -> value"</param>
		/// <returns>The string after replacement</returns>
		public static string ReplaceAfterToken(string source, char token, Tuple<string, object>[] valuePairs)
		{
			if (valuePairs == null)
			{
				throw new ArgumentNullException("valuePairs");
			}
			builder.Clear();

			int sourceLength = source.Length;

			for (int i = 0; i < sourceLength; i++)
			{
				// Append characters until you find the token
				char auxChar = token == char.MaxValue ? (char) (char.MaxValue - 1) : char.MaxValue;
				for (; i < sourceLength && (auxChar = source[i]) != token; i++) builder.Append(auxChar);

				// Ensures no weird stuff regarding token being null
				if (auxChar == token)
				{
					int movePos = 0;

					// Try to find a tuple
					int length = valuePairs.Length;
					for (int ind = 0; ind < length; ind++)
					{
						Tuple<string, object> kvp = valuePairs[ind];
						int j, k;
						for (j = 0, k = i + 1; j < kvp.Item1.Length && k < source.Length && source[k] == kvp.Item1[j]; j++, k++) ;
						// General condition for "key found"
						if (j == kvp.Item1.Length)
						{
							movePos = j;
							builder.Append(kvp.Item2); // append what we're replacing the key with
							break;
						}
					}
					// Don't skip the token if you didn't find the key, append it
					if (movePos == 0) builder.Append(token);
					else i += movePos;
				}
			}

			return builder.ToString();
		}
	}
}
