// <summary>
// 备份生命周期阶段键，由 BackupWorker 在运行过程中逐级上报。
// 对应 ui/i18n.py 中的 stage_* 键（prepare / shadow / archive / copy / done / cancel / error）。
// </summary>
namespace SHBT.Core
{
    /// <summary>备份流水线的阶段键。</summary>
    /// <remarks>
    /// 正常流程为 Prepare → Shadow → Archive → Copy → Done；
    /// 取消时进入 Cancel，任意异常进入 Error。UI 层据此驱动状态显示与本地化文案。
    /// </remarks>
    public enum StageKey
    {
        Prepare,
        Shadow,
        Archive,
        /// <summary>主目标归档完成后，将产物复制到其余勾选目标的阶段（R4）。</summary>
        Copy,
        Done,
        Cancel,
        Error
    }
}
