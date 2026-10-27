using System;

namespace MainGameAgentCamera
{
    /// <summary>
    /// エージェントが投げてくる1コマンド。
    ///
    /// JsonUtility は「クラス1つ = フィールド全部」でしか読めないので、
    /// コマンド種別ごとに型を分けず、1つの平たい入れ物に全項目を持たせる。
    /// 書かれなかった項目は既定値のまま残るため、既定値を「未指定」の意味で使う。
    /// (角度や距離の未指定は NaN 相当の -99999 を使う。0 は正当な指定値なので使えない。)
    /// </summary>
    [Serializable]
    internal sealed class AgentRequest
    {
        internal const float Unset = -99999f;

        /// <summary>応答ファイル名に使う識別子。未指定ならコマンドファイル名を使う。</summary>
        public string id = string.Empty;

        /// <summary>投げた主（claude / codex / grok_build など）。ログと占有の持ち主に使う。</summary>
        public string agent = string.Empty;

        /// <summary>state / targets / look / set / nudge / capture / hold / release / restore / unstick</summary>
        public string cmd = string.Empty;

        // ---- look ----
        /// <summary>face,head,chest,hips,crotch,whole,upper か、生のボーン名。</summary>
        public string target = string.Empty;
        /// <summary>注視点からカメラまでの距離(m)。未指定なら target ごとの既定。</summary>
        public float distance = Unset;
        /// <summary>体の正面を0度とした水平角。+で被写体から見て右へ回り込む。</summary>
        public float yaw = Unset;
        /// <summary>仰角。+で上から見下ろす。</summary>
        public float pitch = Unset;
        /// <summary>カメラのロール(傾き)。</summary>
        public float roll = Unset;
        /// <summary>画角。小さいほど望遠。</summary>
        public float fov = Unset;
        /// <summary>注視点の上下微調整(m)。+で上。</summary>
        public float offsetUp = Unset;
        /// <summary>注視点の左右微調整(m)。+で被写体から見て右。</summary>
        public float offsetRight = Unset;
        /// <summary>注視点の前後微調整(m)。+でカメラ側。</summary>
        public float offsetForward = Unset;

        /// <summary>
        /// 角度の基準。"body"=胴体の正面、"head"=顔の向き、"world"=ワールド。
        /// 未指定なら部位ごとの既定（顔まわりは head、それ以外は body）。
        /// </summary>
        public string frame = string.Empty;

        // ---- place (エージェントが自分で計算した位置をそのまま置く) ----
        /// <summary>注視点のワールド座標。</summary>
        public float[] lookAt;
        /// <summary>カメラのワールド座標。</summary>
        public float[] from;

        /// <summary>
        /// 画面の上向きを体の軸に合わせるか。既定は false ＝地面基準。
        /// カメラは水平に構えるのが基本で、体基準にすると被写体が寝た時に絵ごと転ぶ。
        /// 寝た体を「立っているように」撮りたい特殊な場合だけ true にする。
        /// </summary>
        public bool alignToBody = false;

        /// <summary>被写体が画面に収まる距離まで自動で引くか。既定は入り。</summary>
        public bool autoFit = true;
        /// <summary>手前に別のキャラや物があるとき、抜けている角度を自動で探すか。既定は入り。</summary>
        public bool avoidOccluders = true;

        // ---- nudge (今の構図からの相対指定) ----
        public float dYaw = Unset;
        public float dPitch = Unset;
        public float dRoll = Unset;
        public float dDistance = Unset;
        public float dFov = Unset;
        public float dUp = Unset;
        public float dRight = Unset;

        // ---- set (絶対指定。state が返した値をそのまま戻せる) ----
        public float[] pos;
        public float[] rot;
        public float[] dir;

        // ---- sheet (一覧撮影) ----
        /// <summary>撮る画角の一覧。エージェントが選んだ案をそのまま並べる。</summary>
        public ShotSpec[] shots;
        /// <summary>一覧の横に並べる枚数。</summary>
        public int columns = 5;
        /// <summary>一覧に敷き詰める1枚あたりの大きさ。</summary>
        public int cellWidth = 0;
        public int cellHeight = 0;

        // ---- capture ----
        public int width = 0;
        public int height = 0;
        /// <summary>
        /// 本番の絵から壁の映像パネルを消すか。既定は消さない。
        /// 完成画像には背景がそのまま写っていなければならない。
        /// </summary>
        public bool hideScreens = false;
        /// <summary>
        /// 構図判定用に、映像パネルを消した控えの絵も一緒に出すか。既定は出す。
        /// 背景の映像に引きずられずに構図を見るための、エージェント専用の目。
        /// </summary>
        public bool cleanPreview = true;
        /// <summary>出力ファイル名(拡張子なしでも可)。未指定なら日時から作る。</summary>
        public string name = string.Empty;

        // ---- hold ----
        /// <summary>占有の最長秒数。切れたら自動で解放する（掴みっぱなし防止）。</summary>
        public float seconds = Unset;

        internal static bool HasValue(float v)
        {
            return v > Unset + 1f;
        }

        internal static float Or(float v, float fallback)
        {
            return HasValue(v) ? v : fallback;
        }
    }

    /// <summary>
    /// sheet コマンドで撮る1案。エージェントが「良さそう」と判断した画角を並べて渡す。
    /// 未指定の項目は target の既定値が使われる。
    /// </summary>
    [Serializable]
    internal sealed class ShotSpec
    {
        /// <summary>部位。案ごとに変えてよい。未指定なら親の target。</summary>
        public string target = string.Empty;
        public float yaw = AgentRequest.Unset;
        public float pitch = AgentRequest.Unset;
        public float roll = AgentRequest.Unset;
        public float distance = AgentRequest.Unset;
        public float fov = AgentRequest.Unset;
        /// <summary>注視点の微調整(m)。</summary>
        public float offsetUp = AgentRequest.Unset;
        public float offsetRight = AgentRequest.Unset;
        /// <summary>角度の基準。"head" なら顔の向き基準。</summary>
        public string frame = string.Empty;
    }

    [Serializable]
    internal sealed class Vec3Json
    {
        public float x;
        public float y;
        public float z;

        internal static Vec3Json From(UnityEngine.Vector3 v)
        {
            return new Vec3Json { x = v.x, y = v.y, z = v.z };
        }
    }

    /// <summary>state が返すカメラの現況。set にそのまま渡し戻せる形にしてある。</summary>
    [Serializable]
    internal sealed class CameraStateJson
    {
        public bool hasController;
        /// <summary>注視点(コントローラ基準のローカル座標)。set の pos に対応。</summary>
        public float[] pos;
        /// <summary>角度(オイラー)。set の rot に対応。</summary>
        public float[] rot;
        /// <summary>注視点からカメラへのオフセット。set の dir に対応。</summary>
        public float[] dir;
        public float fov;
        public Vec3Json worldPosition;
        public Vec3Json worldLookAt;
        public float distance;

        // カメラ入力が今止められているか。止めている犯人つき。
        public bool mouseControlBlocked;
        public bool keyControlBlocked;
        public string blockedBy = string.Empty;
    }

    [Serializable]
    internal sealed class SubjectJson
    {
        public bool found;
        public string reason = string.Empty;
        public string[] targets;
        public Vec3Json head;
        /// <summary>胸そのもの(左右バストの中間)。取れない体型なら found_bust=false。</summary>
        public Vec3Json bust;
        public bool foundBust;
        public Vec3Json chest;
        public Vec3Json waist;
        public Vec3Json hips;
        public Vec3Json butt;
        public bool foundButt;
        public Vec3Json crotch;
        public Vec3Json bodyUp;
        public Vec3Json bodyRight;
        /// <summary>体の正面向き。yaw=0 はこの方向からの絵になる。</summary>
        public Vec3Json bodyFront;
        /// <summary>顔が向いている方向。うつむいていても顔を正面から撮れる。</summary>
        public Vec3Json headFront;
        public Vec3Json headUp;
        /// <summary>頭から股までの長さ(m)。距離を決める目安。</summary>
        public float height;

        // ---- 地面 ----
        /// <summary>ワールドの上方向。画面の上はこれに合わせる。</summary>
        public Vec3Json worldUp;
        /// <summary>真下へレイを飛ばして見つけた床の座標。見つからなければ foundFloor=false。</summary>
        public bool foundFloor;
        public Vec3Json floorPoint;
        public string floorName = string.Empty;
        /// <summary>体の一番低い所から床までの高さ(m)。0付近なら接地、大きければ浮いている。</summary>
        public float heightAboveFloor;
        /// <summary>体を包む箱（ワールド）。姿勢に関係ない実際の体の範囲。</summary>
        public Vec3Json boundsMin;
        public Vec3Json boundsMax;
        public Vec3Json boundsSize;
    }

    [Serializable]
    internal sealed class AgentResponse
    {
        public string id = string.Empty;
        public string cmd = string.Empty;
        public bool ok;
        public string error = string.Empty;
        public string message = string.Empty;
        /// <summary>capture の出力先。背景を含む完成画像。</summary>
        public string path = string.Empty;
        /// <summary>構図判定用の控え。壁の映像パネルを消してある。渡す絵ではない。</summary>
        public string cleanPath = string.Empty;
        /// <summary>sheet の一覧画像。番号が焼いてある。</summary>
        public string sheetPath = string.Empty;
        /// <summary>sheet で撮った各案（番号・画角・個別画像のパス）。生JSON。</summary>
        public string shotsJson = string.Empty;
        public CameraStateJson camera;
        public SubjectJson subject;
        public string heldBy = string.Empty;
        /// <summary>bones コマンドの戻り。指定できるボーン名の一覧。</summary>
        public string[] bones;

        // 実際に採用された構図。指示と違う場合は理由が分かるようにする。
        public bool hasApplied;
        public float appliedYaw;
        public float appliedPitch;
        public float appliedDistance;
        public float appliedFov;
        /// <summary>収まりのために引いたか。</summary>
        public bool distanceAdjusted;
        /// <summary>遮蔽を避けて角度をずらしたか。</summary>
        public bool yawAdjusted;
        /// <summary>どう振っても遮蔽が抜けなかった。</summary>
        public bool blocked;
        public string blocker = string.Empty;
    }
}
