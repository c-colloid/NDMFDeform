using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(MeshModifier.NDMFDeform.NDMF.NDMFDeformPlugin))]

namespace MeshModifier.NDMFDeform.NDMF
{
	/// <summary>
	/// NDMFDeform v2 の NDMF プラグイン。
	/// M1 で Transforming フェーズのベイクパス、M2 でプレビュー(IRenderFilter)を実装する。
	/// 旧プラグイン(NDMFPlugin/NDMFDeform.cs)は移行期間中併存する。
	/// </summary>
	public class NDMFDeformPlugin : Plugin<NDMFDeformPlugin>
	{
		public override string QualifiedName => "jp.colloid.ndmfdeform";
		public override string DisplayName => "NDMF Deform";

		protected override void Configure()
		{
			InPhase(BuildPhase.Transforming)
				.Run("Bake Deform Stacks", ctx => BakeDeformStacksPass.Run(ctx))
				.PreviewingWith(new DeformStackPreview());
		}
	}
}
