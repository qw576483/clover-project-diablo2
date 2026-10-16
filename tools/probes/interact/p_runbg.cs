// eval_file：让 Play 循环在编辑器失焦时也全速跑（skill P-2「失焦不 tick」的对策）
UnityEngine.Application.runInBackground = true;
UnityEngine.QualitySettings.vSyncCount = 0;
UnityEngine.Application.targetFrameRate = 60;
UnityEngine.Debug.Log("[RUNBG] runInBackground=" + UnityEngine.Application.runInBackground
    + " vSync=" + UnityEngine.QualitySettings.vSyncCount + " target=" + UnityEngine.Application.targetFrameRate);
CloverEngine.Game.Timer.Every(1f, () => UnityEngine.Debug.Log("[HB] 1s 心跳 t=" + UnityEngine.Time.time.ToString("0.0")));
