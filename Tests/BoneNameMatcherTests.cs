using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// BoneNameMatcher(ボーン名からパーツを推定するヒューリスティック)の検証。
	/// Unity Humanoid、VRoid/VRM、Blender/Rigify、MMD(日本語)、mixamo、Source(ValveBiped)、
	/// 汎用的な衣装命名の各流儀を確認しつつ、左右不明な四肢名や装飾語を含む名前が
	/// 誤ってパーツに割り当てられないこと(None を返すこと)も確認する。
	/// </summary>
	public class BoneNameMatcherTests
	{
		// ---- Unity Humanoid 風 ----
		[TestCase("LeftUpperArm", BodyPart.LeftUpperArm)]
		[TestCase("UpperArm_L", BodyPart.LeftUpperArm)]
		[TestCase("J_01", BodyPart.None)]
		[TestCase("Floating", BodyPart.None)]
		[TestCase("RightLowerLeg", BodyPart.RightLowerLeg)]
		[TestCase("LeftHand", BodyPart.LeftHand)]
		[TestCase("Hips", BodyPart.Torso)]
		[TestCase("Spine", BodyPart.Torso)]
		[TestCase("Chest", BodyPart.Torso)]
		[TestCase("UpperChest", BodyPart.Torso)]
		[TestCase("Neck", BodyPart.Neck)]
		[TestCase("Head", BodyPart.Head)]
		[TestCase("LeftShoulder", BodyPart.LeftShoulder)]
		[TestCase("LeftFoot", BodyPart.LeftFoot)]
		[TestCase("LeftToes", BodyPart.LeftFoot)]
		[TestCase("LeftIndexProximal", BodyPart.LeftHand)]
		[TestCase("RightUpperLeg", BodyPart.RightUpperLeg)]
		// ---- VRoid / VRM ----
		[TestCase("J_Bip_L_UpperArm", BodyPart.LeftUpperArm)]
		[TestCase("J_Bip_R_LowerLeg", BodyPart.RightLowerLeg)]
		[TestCase("J_Bip_C_Hips", BodyPart.Torso)]
		[TestCase("J_Bip_C_Spine", BodyPart.Torso)]
		[TestCase("J_Bip_C_Chest", BodyPart.Torso)]
		[TestCase("J_Bip_L_Hand", BodyPart.LeftHand)]
		[TestCase("J_Bip_L_Thumb1", BodyPart.LeftHand)]
		[TestCase("J_Bip_L_ToeBase", BodyPart.LeftFoot)]
		[TestCase("J_Sec_Hair1_01", BodyPart.None)]
		[TestCase("J_Bip_L_Shoulder", BodyPart.LeftShoulder)]
		// ---- Blender / Rigify ----
		[TestCase("upper_arm.L", BodyPart.LeftUpperArm)]
		[TestCase("forearm.R", BodyPart.RightLowerArm)]
		[TestCase("hand.L", BodyPart.LeftHand)]
		[TestCase("thigh.L", BodyPart.LeftUpperLeg)]
		[TestCase("shin.R", BodyPart.RightLowerLeg)]
		[TestCase("foot.L", BodyPart.LeftFoot)]
		[TestCase("toe.L", BodyPart.LeftFoot)]
		[TestCase("spine", BodyPart.Torso)]
		[TestCase("spine.001", BodyPart.Torso)]
		[TestCase("DEF-upper_arm.L", BodyPart.LeftUpperArm)]
		[TestCase("shoulder.L", BodyPart.LeftShoulder)]
		[TestCase("ORG-thigh.L", BodyPart.LeftUpperLeg)]
		[TestCase("MCH-foot.R", BodyPart.RightFoot)]
		// ---- MMD / 日本語 ----
		[TestCase("左腕", BodyPart.LeftUpperArm)]
		[TestCase("右ひじ", BodyPart.RightLowerArm)]
		[TestCase("左手首", BodyPart.LeftHand)]
		[TestCase("左足", BodyPart.LeftUpperLeg)]
		[TestCase("右ひざ", BodyPart.RightLowerLeg)]
		[TestCase("左足首", BodyPart.LeftFoot)]
		[TestCase("上半身", BodyPart.Torso)]
		[TestCase("上半身2", BodyPart.Torso)]
		[TestCase("下半身", BodyPart.Torso)]
		[TestCase("首", BodyPart.Neck)]
		[TestCase("頭", BodyPart.Head)]
		[TestCase("左肩", BodyPart.LeftShoulder)]
		[TestCase("左親指０", BodyPart.LeftHand)]
		[TestCase("左目", BodyPart.Head)]
		[TestCase("センター", BodyPart.None)]
		[TestCase("左スカート", BodyPart.None)]
		[TestCase("髪", BodyPart.None)]
		// ---- 衣装(コスチューム)風 ----
		[TestCase("Arm_L", BodyPart.LeftUpperArm)]
		[TestCase("Leg_R", BodyPart.RightUpperLeg)]
		[TestCase("Elbow_L", BodyPart.LeftLowerArm)]
		[TestCase("Knee_R", BodyPart.RightLowerLeg)]
		[TestCase("Foot_L", BodyPart.LeftFoot)]
		[TestCase("Hand_R", BodyPart.RightHand)]
		[TestCase("Shoulder_L", BodyPart.LeftShoulder)]
		[TestCase("L_UpperArm", BodyPart.LeftUpperArm)]
		[TestCase("R_Thigh", BodyPart.RightUpperLeg)]
		[TestCase("LUpperArm", BodyPart.LeftUpperArm)]
		[TestCase("RThigh", BodyPart.RightUpperLeg)]
		[TestCase("Skirt_1_L", BodyPart.None)]
		[TestCase("Ribbon_L", BodyPart.None)]
		[TestCase("Sleeve_L", BodyPart.None)]
		[TestCase("Breast_L", BodyPart.Torso)]
		[TestCase("Bust_R", BodyPart.Torso)]
		[TestCase("Chest_Ribbon", BodyPart.None)]
		// ---- mixamo ----
		[TestCase("mixamorig:LeftForeArm", BodyPart.LeftLowerArm)]
		[TestCase("mixamorig:Spine2", BodyPart.Torso)]
		[TestCase("mixamorig:RightHand", BodyPart.RightHand)]
		// ---- Source / ValveBiped ----
		[TestCase("ValveBiped.Bip01_R_Hand", BodyPart.RightHand)]
		[TestCase("Character1_LeftUpperArm", BodyPart.LeftUpperArm)]
		// ---- 判断できない・パーツではない(None) ----
		[TestCase("Arm", BodyPart.None)]
		[TestCase("Leg", BodyPart.None)]
		[TestCase("Root", BodyPart.None)]
		[TestCase("Armature", BodyPart.None)]
		[TestCase("Bone.001", BodyPart.None)]
		[TestCase("Handle_L", BodyPart.None)]
		[TestCase("Chair", BodyPart.None)]
		[TestCase("Tail_01", BodyPart.None)]
		[TestCase("Ear_L", BodyPart.None)]
		[TestCase("", BodyPart.None)]
		[TestCase(null, BodyPart.None)]
		public void Match_ReturnsExpectedBodyPart(string boneName, BodyPart expected)
		{
			Assert.That(BoneNameMatcher.Match(boneName), Is.EqualTo(expected));
		}
	}
}
