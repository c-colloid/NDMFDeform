using System.Collections.Generic;
using System.Text;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// ボーン名から体のパーツを推定するヒューリスティック。
	/// 衣装(アウトフィット)側のアーマチュアは Unity Humanoid、VRoid/VRM、Blender/Rigify、
	/// MMD、mixamo、Source(ValveBiped) など命名規則がバラバラなため、既知の接頭辞を剥がし、
	/// 記号・camelCase・文字種(英字/数字/CJK)の境界でトークン化してからパーツ語彙と照合する。
	/// これは「衣装ボーンがどのパーツに属するか」を判定するための一証拠に過ぎないので、
	/// 誤判定(別パーツへの取り違え)より「わからない(None)」を優先する。
	/// 具体的には: 左右が判別できない四肢の名前(例: "Arm")、スカート/リボン等の装飾語を
	/// 含む名前、既知の語彙に一致しない名前は、すべて BodyPart.None を返す。
	/// </summary>
	public static class BoneNameMatcher
	{
		private enum Side
		{
			None,
			Left,
			Right,
		}

		private enum PartKind
		{
			None,
			Torso,
			Neck,
			Head,
			Shoulder,
			UpperArm,
			LowerArm,
			Hand,
			UpperLeg,
			LowerLeg,
			Foot,
		}

		private enum CharClass
		{
			AsciiLetter,
			Digit,
			Other,
		}

		/// <summary>剥がす既知の接頭辞(大文字小文字は無視)。長いもの/具体的なものを先に判定する</summary>
		private static readonly string[] KnownPrefixes =
		{
			"ValveBiped.Bip01_",
			"mixamorig:",
			"Armature|",
			"J_Bip_",
			"J_Sec_",
			"Character1_",
			"Bone_",
			"DEF-",
			"ORG-",
			"MCH-",
			"Bip01",
		};

		/// <summary>手・指(まとめて手として扱う)</summary>
		private static readonly HashSet<string> HandTokens = new HashSet<string>
		{
			"hand", "wrist", "thumb", "index", "middle", "ring", "little", "pinky",
		};

		/// <summary>足首から先(つま先含む)</summary>
		private static readonly HashSet<string> FootTokens = new HashSet<string>
		{
			"foot", "feet", "toe", "toes", "ankle",
		};

		/// <summary>体幹(Hips〜Chest、胸/バスト含む)</summary>
		private static readonly HashSet<string> TorsoTokens = new HashSet<string>
		{
			"hips", "hip", "pelvis", "spine", "chest", "upperchest", "torso", "breast", "bust",
		};

		/// <summary>頭部(顔のパーツ含む)</summary>
		private static readonly HashSet<string> HeadTokens = new HashSet<string>
		{
			"head", "face", "jaw", "eye",
		};

		/// <summary>
		/// 装飾・付属品のトークン(英語)。これらが含まれる場合、他パーツ語と同時に出現しても
		/// 常に None を返す(例: "Chest_Ribbon" はリボンという装飾物であって胸そのものではない)
		/// </summary>
		private static readonly HashSet<string> DecorationTokens = new HashSet<string>
		{
			"skirt", "ribbon", "sleeve", "hair", "tail", "ear", "cloth", "cape",
			"accessory", "bag", "strap", "belt", "collar", "frill", "tie", "scarf", "wing",
		};

		/// <summary>装飾・付属品のキーワード(日本語)。部分一致で判定する</summary>
		private static readonly string[] DecorationKeywordsJp =
		{
			"スカート", "リボン", "袖", "髪", "尻尾", "耳", "布", "マント",
		};

		/// <summary>
		/// 日本語のパーツキーワード(部分一致・優先順)。
		/// 「手首」「足首」等の複合語は、それに含まれる短い語(「手」「首」「足」)より先に判定する必要がある。
		/// </summary>
		private static readonly (string Keyword, PartKind Kind)[] PartKeywordsJp =
		{
			("手首", PartKind.Hand),
			("足首", PartKind.Foot),
			("足先", PartKind.Foot),
			("つま先", PartKind.Foot),
			("上半身", PartKind.Torso),
			("下半身", PartKind.Torso),
			("前腕", PartKind.LowerArm),
			("上腕", PartKind.UpperArm),
			("太もも", PartKind.UpperLeg),
			("親指", PartKind.Hand),
			("人差し指", PartKind.Hand),
			("人指", PartKind.Hand),
			("中指", PartKind.Hand),
			("薬指", PartKind.Hand),
			("小指", PartKind.Hand),
			("肩", PartKind.Shoulder),
			("肘", PartKind.LowerArm),
			("ひじ", PartKind.LowerArm),
			("膝", PartKind.LowerLeg),
			("ひざ", PartKind.LowerLeg),
			("すね", PartKind.LowerLeg),
			("腕", PartKind.UpperArm),
			("脚", PartKind.UpperLeg),
			("足", PartKind.UpperLeg),
			("手", PartKind.Hand),
			("首", PartKind.Neck),
			("頭", PartKind.Head),
			("顔", PartKind.Head),
			("目", PartKind.Head),
			("胸", PartKind.Torso),
			("腰", PartKind.Torso),
		};

		/// <summary>ボーン名から体のパーツを推定する(名前ヒューリスティック)。判断できなければ BodyPart.None</summary>
		public static BodyPart Match(string boneName)
		{
			if (string.IsNullOrWhiteSpace(boneName))
				return BodyPart.None;

			var stripped = StripKnownPrefixes(boneName.Trim());
			var tokens = Tokenize(stripped);
			if (tokens.Count == 0)
				return BodyPart.None;

			// 装飾・付属品の語が含まれていれば、他がどれだけパーツらしく見えても None
			if (ContainsDecoration(tokens))
				return BodyPart.None;

			var side = DetectSide(tokens);

			var asciiSet = new HashSet<string>();
			var otherTokens = new List<string>();
			foreach (var token in tokens)
			{
				if (IsAsciiToken(token))
					asciiSet.Add(Lower(token));
				else
					otherTokens.Add(token);
			}

			var kind = DetectKindAscii(asciiSet);
			if (kind == PartKind.None)
				kind = DetectKindJapanese(otherTokens);

			return Resolve(kind, side);
		}

		private static string StripKnownPrefixes(string name)
		{
			// 複数の接頭辞が重なる可能性は低いが、念のため数回まで繰り返し剥がす
			for (var guard = 0; guard < 4; guard++)
			{
				var strippedAny = false;
				foreach (var prefix in KnownPrefixes)
				{
					if (name.Length >= prefix.Length &&
						name.Substring(0, prefix.Length).Equals(prefix, System.StringComparison.OrdinalIgnoreCase))
					{
						name = name.Substring(prefix.Length);
						strippedAny = true;
						break;
					}
				}
				if (!strippedAny)
					break;
			}
			return name;
		}

		private static bool ContainsDecoration(List<string> tokens)
		{
			foreach (var token in tokens)
			{
				if (IsAsciiToken(token))
				{
					if (DecorationTokens.Contains(Lower(token)))
						return true;
				}
				else
				{
					foreach (var keyword in DecorationKeywordsJp)
					{
						if (token.Contains(keyword))
							return true;
					}
				}
			}
			return false;
		}

		private static Side DetectSide(List<string> tokens)
		{
			foreach (var token in tokens)
			{
				if (IsAsciiToken(token))
				{
					var lower = Lower(token);
					if (lower == "l" || lower == "left")
						return Side.Left;
					if (lower == "r" || lower == "right")
						return Side.Right;
				}
				else if (token.Length > 0)
				{
					if (token[0] == '左')
						return Side.Left;
					if (token[0] == '右')
						return Side.Right;
				}
			}
			return Side.None;
		}

		private static PartKind DetectKindAscii(HashSet<string> tokens)
		{
			if (tokens.Overlaps(HandTokens))
				return PartKind.Hand;
			if (tokens.Contains("elbow") || tokens.Contains("forearm") || tokens.Contains("lowerarm"))
				return PartKind.LowerArm;
			if (tokens.Contains("knee") || tokens.Contains("shin") || tokens.Contains("calf") || tokens.Contains("lowerleg"))
				return PartKind.LowerLeg;
			if (tokens.Contains("thigh") || tokens.Contains("upperleg"))
				return PartKind.UpperLeg;
			if (tokens.Overlaps(FootTokens))
				return PartKind.Foot;
			if (tokens.Contains("shoulder") || tokens.Contains("clavicle"))
				return PartKind.Shoulder;
			if (tokens.Contains("arm") || tokens.Contains("bicep") || tokens.Contains("upperarm"))
				return tokens.Contains("fore") || tokens.Contains("lower") ? PartKind.LowerArm : PartKind.UpperArm;
			if (tokens.Contains("leg"))
				return tokens.Contains("lower") || tokens.Contains("shin") ? PartKind.LowerLeg : PartKind.UpperLeg;
			if (tokens.Overlaps(TorsoTokens))
				return PartKind.Torso;
			if (tokens.Contains("neck"))
				return PartKind.Neck;
			if (tokens.Overlaps(HeadTokens))
				return PartKind.Head;
			return PartKind.None;
		}

		private static PartKind DetectKindJapanese(List<string> otherTokens)
		{
			foreach (var raw in otherTokens)
			{
				var s = raw;
				if (s.Length > 0 && (s[0] == '左' || s[0] == '右'))
					s = s.Substring(1);
				if (s.Length == 0)
					continue;

				foreach (var (keyword, kind) in PartKeywordsJp)
				{
					if (s.Contains(keyword))
						return kind;
				}
			}
			return PartKind.None;
		}

		private static BodyPart Resolve(PartKind kind, Side side)
		{
			switch (kind)
			{
				case PartKind.Torso: return BodyPart.Torso;
				case PartKind.Neck: return BodyPart.Neck;
				case PartKind.Head: return BodyPart.Head;
				case PartKind.Shoulder: return SidePart(side, BodyPart.LeftShoulder, BodyPart.RightShoulder);
				case PartKind.UpperArm: return SidePart(side, BodyPart.LeftUpperArm, BodyPart.RightUpperArm);
				case PartKind.LowerArm: return SidePart(side, BodyPart.LeftLowerArm, BodyPart.RightLowerArm);
				case PartKind.Hand: return SidePart(side, BodyPart.LeftHand, BodyPart.RightHand);
				case PartKind.UpperLeg: return SidePart(side, BodyPart.LeftUpperLeg, BodyPart.RightUpperLeg);
				case PartKind.LowerLeg: return SidePart(side, BodyPart.LeftLowerLeg, BodyPart.RightLowerLeg);
				case PartKind.Foot: return SidePart(side, BodyPart.LeftFoot, BodyPart.RightFoot);
				default: return BodyPart.None;
			}
		}

		/// <summary>左右が要るパーツで、左右が判別できなければ None(誤判定より「わからない」を優先)</summary>
		private static BodyPart SidePart(Side side, BodyPart left, BodyPart right)
		{
			if (side == Side.Left)
				return left;
			if (side == Side.Right)
				return right;
			return BodyPart.None;
		}

		private static bool IsAsciiToken(string token)
		{
			foreach (var c in token)
			{
				if (c > 127)
					return false;
			}
			return true;
		}

		private static string Lower(string token)
		{
			return token.ToLowerInvariant();
		}

		/// <summary>
		/// 区切り文字(_ - . | : 空白 等)と、camelCase / 数字 / CJK の文字種境界でトークン化する。
		/// 数字のみのトークン(連番・バージョン番号。全角数字も含む)は除外する。
		/// </summary>
		private static List<string> Tokenize(string name)
		{
			var tokens = new List<string>();
			var current = new StringBuilder();
			CharClass? previousClass = null;

			for (var i = 0; i < name.Length; i++)
			{
				var c = name[i];
				if (IsSeparator(c))
				{
					Flush(tokens, current);
					previousClass = null;
					continue;
				}

				var cls = ClassOf(c);
				var boundary = false;
				if (previousClass.HasValue)
				{
					if (previousClass.Value != cls)
					{
						// 数字 <-> 英字、英字 <-> CJK 等の文字種の切り替わり
						boundary = true;
					}
					else if (cls == CharClass.AsciiLetter)
					{
						var previous = name[i - 1];
						if (char.IsLower(previous) && char.IsUpper(c))
						{
							// 小文字 → 大文字: 新しい単語の始まり("leftUpper" の U 前)
							boundary = true;
						}
						else if (char.IsUpper(previous) && char.IsUpper(c) && i + 1 < name.Length && char.IsLower(name[i + 1]))
						{
							// 連続する大文字の末尾: 直後が小文字なら、その直前で区切る
							// (例: "LUpperArm" -> "L" | "Upper" | "Arm")
							boundary = true;
						}
					}
				}

				if (boundary)
					Flush(tokens, current);

				current.Append(c);
				previousClass = cls;
			}
			Flush(tokens, current);

			tokens.RemoveAll(IsAllDigits);
			return tokens;
		}

		private static void Flush(List<string> tokens, StringBuilder current)
		{
			if (current.Length > 0)
			{
				tokens.Add(current.ToString());
				current.Clear();
			}
		}

		private static bool IsSeparator(char c)
		{
			if (c == '_' || c == '-' || c == '.' || c == '|' || c == ':')
				return true;
			if (char.IsWhiteSpace(c))
				return true;
			// 英字でも数字でもない記号(カンマ、括弧等)も区切りとして扱う
			return !char.IsLetterOrDigit(c);
		}

		private static CharClass ClassOf(char c)
		{
			if (char.IsDigit(c))
				return CharClass.Digit;
			if (c <= 127)
				return CharClass.AsciiLetter;
			return CharClass.Other;
		}

		private static bool IsAllDigits(string token)
		{
			if (token.Length == 0)
				return false;
			foreach (var c in token)
			{
				if (!char.IsDigit(c))
					return false;
			}
			return true;
		}
	}
}
