using System;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 收起／展開動作進行中的執行者。
    ///
    /// 兼容模組（例如 CE 的彈藥同步）在轉換過程中可能產生溢出物，需要知道該交給誰；
    /// 但轉換當下來源與目標 Thing 都已經脫離持有者、也還沒生成到地圖上，無從回推。
    /// 因此由發起動作的程式碼在轉換期間掛上這層 context 供兼容層查詢。
    ///
    /// 單執行緒使用；以 <see cref="IDisposable"/> 搭配 using 確保即使中途丟例外也會還原。
    /// </summary>
    public static class DeployContext
    {
        /// <summary>目前正在收起或展開可部署物的 pawn；不在轉換流程中時為 null。</summary>
        public static Pawn CurrentWorker { get; private set; }

        public static IDisposable Push(Pawn worker)
        {
            return new Scope(worker);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Pawn previous;
            private bool disposed;

            internal Scope(Pawn worker)
            {
                previous = CurrentWorker;
                CurrentWorker = worker;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                CurrentWorker = previous;
            }
        }
    }
}
