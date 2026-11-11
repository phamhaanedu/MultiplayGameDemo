# Game Concept: Capsule Arena: Last Man Standing

*Created: 2026-11-10*
*Status: Approved (REVISED 2026-11-10 — Multi-Directional Melee, Keybinding JSON/UI, Mobile-Ready On-Screen Skill Buttons & ScriptableObject Stats)*

> **Creative Director Review (CD-PILLARS)**: APPROVED 2026-11-10
> **Art Director Review (AD-CONCEPT-VISUAL)**: STRONG 2026-11-10
> **Technical Director Review (TD-FEASIBILITY)**: VIABLE 2026-11-10
> **Producer Review (PR-SCOPE)**: REALISTIC 2026-11-10

---

## Elevator Pitch

> *Capsule Arena: Last Man Standing* là một game hành động sinh tồn đấu trường 3D (Arena Battle Royale / Last Man Standing) nhiều người chơi trên Unity 6 (URP) và Photon Fusion 2 Shared Mode, nơi bạn điều khiển chiến binh Cartoon Capsule kết hợp nhuần nhuyễn hệ thống kỹ năng cơ động/chiến đấu đa hướng (di chuyển `WASD` 360°, xoay theo chuột, lướt tiến **Forward Dash phím `2`**, lăn né ngang **Side Dodge Roll `Q`/`E`**, **chém dọc `Left Click`**, **chém ngang xoay người $90^\circ$ phím `1`/`3`** và **bắn tầm xa `Right Click`**) cùng cụm nút bấm mềm trên màn hình sẵn sàng cho Mobile và khả năng chuyển đổi góc nhìn **FPS $\leftrightarrow$ TPS** tức thì để trở thành người sống sót cuối cùng trên bảng xếp hạng PlayFab.

---

## Core Identity

| Aspect | Detail |
| ---- | ---- |
| **Genre** | 3D Arena Action Brawler / Last Man Standing Battle Royale |
| **Platform** | PC (Windows Standalone — Keyboard & Mouse) + **Mobile-Ready HUD Architecture** (On-Screen Skill Buttons) |
| **Target Audience** | Mid-core PvP / Arena Action players & GAM302 Multiplayer Evaluators |
| **Player Count** | Multiplayer (2–8+ players per room via Photon Fusion 2 `GameMode.Shared`) |
| **Session Length** | 15–30 minutes (3–5 minutes per Last Man Standing match) |
| **Monetization** | None (Academic Course Demo — GAM302) |
| **Estimated Scope** | Small (3–4 weeks, solo) |
| **Comparable Titles** | *Brawl Stars*, *PUBG / Fortnite* (Arena Tactical Cover), *Fall Guys* (Capsule Spectating & Showdown) |

---

## Core Fantasy

Trở thành chiến binh Capsule cuối cùng trụ vững trên đấu trường sinh tử 3D nhờ sự kết hợp điêu luyện giữa phản xạ cơ động (**Forward Dash phím `2`**, **Side Dodge Roll `Q`/`E`**), hệ thống cận chiến đa hướng (**Chém dọc `Left Click`** & **Chém ngang quét quạt $90^\circ$ kèm xoay người bằng phím `1`/`3`**), hỏa lực tầm xa (**Ranged Attack `Right Click`** có bù trễ mạng), nhãn quan chiến thuật chuyển đổi góc nhìn **FPS $\leftrightarrow$ TPS** tức thì quanh các khối vật cản (`Obstacle`), dưới sự theo dõi và bình luận trực tiếp từ những người chơi đã hóa **Hồn ma (Ghost Mode Spectator)** trên kênh Chat.

---

## Unique Hook

Giống *Brawl Stars* kết hợp *PUBG / Fall Guys* thu nhỏ ở nhịp độ sinh tồn đấu trường nhiều người chơi sôi động, **VÀ CÒN** cho phép chuyển đổi góc nhìn **FPS $\leftrightarrow$ TPS** tức thời sau các khối `Obstacle`, vận hành hoàn toàn trên kiến trúc mạng phi tập trung **Photon Fusion 2 — Shared Mode (`GameMode.Shared`, `StateAuthority` + `LagCompensation`)**, tích hợp liền mạch **Ghost Mode Spectator**, **Public/Private Whisper Chat** và **PlayFab Cloud Leaderboard** thời gian thực.

---

## Player Experience Analysis (MDA Framework)

### Target Aesthetics (What the player FEELS)

| Aesthetic | Priority | How We Deliver It |
| ---- | ---- | ---- |
| **Challenge** (obstacle course, mastery) | **1 (Primary)** | Phản xạ lăn né đòn (`Q/E`), quản lý Cooldown (`TickTimer`) của Dash, chuyển góc nhìn FPS/TPS hợp lý và định vị sau vật cản để sinh tồn đến cuối cùng. |
| **Fellowship** (social connection) | **2 (Secondary)** | Giao tiếp trực tiếp qua Public Chat & Private Whisper (`CanvasGroup`), cùng theo dõi và cổ vũ những người còn sống qua chế độ **Ghost Mode Spectator**. |
| **Sensation** (sensory pleasure) | **3 (Supporting)** | Hiệu ứng **DOTween Squash & Stretch** đàn hồi khi lướt/lăn né, VFX trúng đòn (`Rpc_PlayAttackEffect`) sắc nét và âm thanh không gian 3D (`Spatial Blend = 1.0`) sống động. |
| **Fantasy** (make-believe, role-playing) | **4 (Supporting)** | Hóa thân thành đấu sĩ Capsule mang tên định danh riêng (`[Networked] NetworkString<_16> PlayerName`) tranh ngôi vương trên bảng vàng PlayFab. |
| **Discovery** (exploration, secrets) | **5 (Minimal)** | Khám phá các góc bắn hẹp (sightlines) và vị trí ẩn nấp tối ưu quanh các khối `Obstacle` trên đấu trường khi chuyển đổi giữa FPS và TPS. |
| **Expression** (self-expression, creativity) | **6 (Minimal)** | Thể hiện phong cách chiến đấu cá nhân (cận chiến áp sát tốc độ cao hay bắn tỉa kiểm soát cự ly) và giao tiếp cá tính trên kênh Chat. |
| **Narrative** (drama, story arc) | **N/A** | Không có cốt truyện tuyến tính; kịch tính đến từ diễn biến sinh tồn tự nhiên của từng ván đấu (Emergent Match Drama). |
| **Submission** (relaxation, comfort zone) | **N/A** | Trò chơi tập trung vào nhịp độ giao tranh tập trung cao độ, chỉ thư giãn khi đã chuyển sang Ghost Mode quan sát. |

### Key Dynamics (Emergent player behaviors)

1. **Đấu trí cự ly, Góc chém & Cooldown (Spacing, Directional Slash & Cooldown Baiting):** Người chơi chủ động nhử đối phương tung đạn bắn xa (`Right Click`), dùng `Side Dodge Roll (Q/E)` lăn né ngang rồi bấm **phím `2` (`Forward Dash`)** áp sát để tung **Chém dọc (`Left Click`)** sát thương trực diện, hoặc khi đối phương lăn né sang hông thì lập tức bấm **phím `1` / `3` (Chém ngang hình quạt $90^\circ$ trái/phải)** để vừa quét trúng mục tiêu vừa xoay mặt nhân vật $90^\circ$ bắt kịp hướng di chuyển của địch.
2. **Chuyển đổi góc nhìn theo địa hình (Tactical Camera Toggling):** Khi nấp sau khối `Obstacle` Cube, người chơi bật **TPS** để quan sát đối thủ di chuyển mà không lộ thân mình; khi bước ra khe hẹp để bắn tầm xa (`Right Click`), người chơi bấm `V` chuyển sang **FPS** để ngắm chuẩn xác.
3. **Khán đài bình luận trực tiếp (Active Ghost Spectating):** Những người chơi bị hạ gục sớm không rời phòng mà chuyển camera qua lại giữa những người còn sống (`Next / Previous`), dùng kênh **Public Chat** để bình luận kịch tính hoặc **Private Chat (Whisper)** để nhắn tin riêng, tạo không khí giải đấu sôi nổi cho tới khi tìm ra **Winner**.

### Core Mechanics (Systems we build)

1. **Photon Fusion 2 Shared Mode Architecture (`GameMode.Shared`):** Quản lý phiên chơi qua `NetworkRunner` (AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`), chọn Region cân bằng tải, đặt tên nhân vật tại Lobby, khởi tạo nhân vật qua `Runner.Spawn()` với `StateAuthority` thuộc về từng Client và `MatchManager` thuộc về `SharedModeMasterClient`.
2. **Tick-Accurate Character & Multi-Directional Combat Controller (`FixedUpdateNetwork()`):**
   - **Di chuyển 360° & Xoay hướng:** Di chuyển bằng cụm phím `WASD` và xoay góc mặt theo chuột (`NetworkTransform`).
   - **Lướt tiến (`Forward Dash` — Phím mặc định: `2`):** Phóng nhanh về phía trước một khoảng ngắn, quản lý thời gian lướt và Cooldown bằng `[Networked] TickTimer`.
   - **Lăn né ngang (`Side Dodge Roll` — Phím mặc định: `Q` lăn trái, `E` lăn phải):** Lăn né đòn sang hai bên kèm hiệu ứng nghiêng/đàn hồi thân người (**DOTween Squash & Stretch**).
   - **Đánh cận chiến nâng cao (`Melee Attack` — 2 loại chiêu thức / 3 hướng đánh):**
     - **Nhát chém dọc (`Vertical Slash` — Mặc định: `Left Click`):** Quét vùng va chạm hình quạt dọc thẳng từ trên xuống ngay phía trước mặt nhân vật (tối ưu sát thương đơn mục tiêu trực diện).
     - **Nhát chém ngang xoay người $90^\circ$ (`Horizontal Turning Slash` — Mặc định: Phím `1` chém trái, Phím `3` chém phải):** Quét vùng va chạm hình quạt ngang góc $90^\circ$ từ chính diện sang trái (phím `1`) hoặc sang phải (phím `3`); đồng thời **xoay mượt đồng bộ cả Nhân vật (`NetworkTransform`) lẫn Camera Cinemachine (`FPS` / `TPS`) $90^\circ$ theo hướng chém trong khoảng `~0.15s`** (thực thi theo Tick mạng trong `FixedUpdateNetwork()`), giúp giữ trải nghiệm liền mạch, vừa khắc chế kẻ địch lăn né sang hông vừa đổi góc nhìn chiến thuật tức thời.
   - **Đánh xa (`Ranged Attack` — Mặc định: `Right Click`):** Bắn đạn tầm xa về phía trước theo hướng ngắm, ứng dụng `Runner.Spawn()` kết hợp **Object Pooling (`INetworkObjectProvider`)**.
3. **Data-Driven Configuration (`ScriptableObject`) & Customizable Keybinding System (`keybinding.json` + Rebind UI):**
   - **Cấu hình thông số bằng `ScriptableObject` (`CharacterCombatConfigSO`):** Toàn bộ thông số nhân vật và kỹ năng — bao gồm Máu tối đa (`MaxHealth`), Tốc độ di chuyển (`MoveSpeed`), Tốc độ & Thời gian lướt (`DashSpeed`, `DashDuration`, `DashCooldown`), Tốc độ & Thời gian lăn né (`DodgeSpeed`, `DodgeDuration`, `DodgeCooldown`), Sát thương của từng loại đòn đánh (`VerticalSlashDamage`, `HorizontalSlashDamage`, `RangedDamage`), Tầm xa & Góc quạt cận chiến (`MeleeRange`, `VerticalSlashAngle`, `HorizontalSlashAngle = 90°`, `HorizontalTurnDuration = 0.15s`), Tầm bay & Tốc độ đạn đánh xa (`ProjectileSpeed`, `ProjectileMaxDistance`), và Cooldown của từng chiêu — đều được đọc từ **file `ScriptableObject`** để tinh chỉnh cân bằng dễ dàng mà không sửa code.
   - **Hệ thống cài đặt & gán phím linh hoạt (`keybinding.json` & In-Game Rebind UI):**
     - **Nạp đầu game (Loading Scene Bootstrap):** Ngay ở đầu game tại **Loading Scene**, hệ thống gọi đọc file `keybinding.json` để nạp và gán toàn bộ phím điều khiển (`WASD`, `Left Click`, `Right Click`, `1`, `2`, `3`, `Q`, `E`, `V`, `Enter`). Nếu file chưa tồn tại, hệ thống tự động tạo `keybinding.json` từ bộ phím mặc định.
     - **Giao diện gán phím trực tiếp (In-Game Keybinding Rebind UI):** Khi người chơi mở bảng UI gán phím, thay đổi phím bấm cho bất kỳ chức năng nào và bấm **Lưu (Save)**, hệ thống lập tức cập nhật cấu hình phím đang chạy, đồng bộ nhãn phím hiển thị trên cụm Button mềm góc dưới phải và **ghi lưu kết quả mới vào file `keybinding.json`**.
   - **Cụm nút bấm mềm trên màn hình (Mobile-Ready On-Screen Action Buttons):** Tất cả các chiêu thức (`Forward Dash [2]`, `Dodge Left [Q]`, `Dodge Right [E]`, `Vertical Slash [LMB]`, `Horizontal Slash Left [1]`, `Horizontal Slash Right [3]`, `Ranged Shot [RMB]`) đều có **Button mềm hiển thị ở góc dưới bên phải màn hình** giống các game hành động trên Mobile (hiển thị icon chiêu thức, vòng đếm ngược Cooldown `TickTimer` và nhãn phím tắt tự động cập nhật theo Keybinding), cho phép kích hoạt bằng cả phím cứng lẫn thao tác bấm/chạm trên màn hình và sẵn sàng chuyển đổi sang nền tảng Mobile sau này.
4. **Hybrid Hit Detection, Lag Compensation & RPC Combat:** Thiết lập `HitboxRoot` & `Hitbox` kết hợp `Runner.LagCompensation` và Physics Cast theo Layer (`Player`, `Projectile`, `Obstacle`, `Boundary`), gây sát thương chuẩn quyền hạn qua `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` (`Rpc_TakeDamage`) và đồng bộ hiệu ứng chém dọc/ngang, đường đạn cùng âm thanh 3D qua `[Rpc(RpcSources.StateAuthority, RpcTargets.All)]` (`Rpc_PlayAttackEffect`).
5. **Dual-Perspective Cinemachine (`FPS <-> TPS`) & Ghost Mode Spectator:** Gắn `CinemachineCamera` cho nhân vật có `HasStateAuthority`, bấm phím `V` hoán đổi mượt mà giữa góc nhìn thứ nhất và góc nhìn thứ ba (tự động đồng bộ góc xoay camera khi nhân vật xoay $90^\circ$ sau đòn chém ngang phím `1`/`3`); khi `CurrentHealth <= 0` (`IsDead = true`), tự động chuyển camera sang theo dõi những người chơi còn sống kèm nút chuyển mục tiêu.
6. **Event-Driven UI (`[OnChangedRender]`), In-Game Chat & PlayFab Cloud:** World-Space Billboard UI trên đầu nhân vật (Tên + Thanh máu `100/100`), Screen-Space HUD (Máu bản thân, cụm On-Screen Skill Buttons góc dưới phải, `MatchTimer`, Scoreboard `INetworkStruct`), khung Chat Public/Whisper tối ưu bằng `CanvasGroup` (khóa input chiến đấu khi nhấn `Enter`), và tích hợp **PlayFab SDK** lưu trữ `Player Data / Title Data` cùng bảng xếp hạng **Leaderboard (`Wins` / `Kills`)**.

---

## Player Motivation Profile

### Primary Psychological Needs Served

| Need | How This Game Satisfies It | Strength |
| ---- | ---- | ---- |
| **Competence** (mastery, skill growth) | Làm chủ bộ 6 kỹ năng di chuyển/chiến đấu, căn chuẩn thời gian hồi chiêu (`TickTimer`), lăn né đạn đẹp mắt và chiến thắng công bằng nhờ đồng bộ Tick mạng chuẩn xác. | **Core** |
| **Autonomy** (freedom, meaningful choice) | Tự do lựa chọn góc nhìn (**FPS** hay **TPS**), lối đánh (áp sát cận chiến hay cấu rỉa tầm xa) và cách tận dụng các khối `Obstacle` trên đấu trường. | **Core** |
| **Relatedness** (connection, belonging) | Kết nối trực tiếp với bạn cùng phòng qua hệ thống **Public Chat / Private Whisper**, chế độ **Ghost Mode Spectator** và cuộc đua thứ hạng trên **PlayFab Leaderboard**. | **Supporting** |

### Player Type Appeal (Bartle Taxonomy)

- [x] **Killers/Competitors** (domination, PvP, leaderboards) — **How:** Đối kháng sinh tồn trực diện đến người cuối cùng (`Last Man Standing`), tối ưu số mạng hạ gục (`Kills`) và số trận thắng (`Wins`) trên bảng xếp hạng PlayFab.
- [x] **Achievers** (goal completion, collection, progression) — **How:** Theo dõi thành tích cá nhân được lưu trữ bền vững trên PlayFab Cloud (`Player Data` & `Leaderboards`) và hoàn thành các ván đấu với lượng máu bảo toàn cao nhất.
- [x] **Socializers** (relationships, cooperation, community) — **How:** Tương tác thời gian thực qua kênh Chat chung và nhắn tin mật (Whisper), đồng thời tận hưởng việc xem và bình luận trận đấu ở chế độ Ghost Mode.
- [ ] **Explorers** (discovery, understanding systems, finding secrets) — **How:** Chỉ đóng vai trò phụ thông qua việc tìm góc đứng và đường đạn tối ưu quanh các khối vật cản trong đấu trường.

### Flow State Design

- **Onboarding curve:** Ngay tại **Loading Scene** đầu game, hệ thống tự động đọc file `keybinding.json` để gán phím điều khiển; sang màn hình Lobby trực quan (nhập Tên, chọn Region, hoặc mở bảng UI gán phím Rebind và bấm Lưu để ghi đè vào `keybinding.json`, rồi bấm Start); vào trận có ngay HUD rõ ràng kèm cụm **Button chiêu thức mềm ở góc dưới phải màn hình** và thao tác phím mặc định (`WASD`, Chuột xoay mặt, `Left Click` chém dọc, `Right Click` bắn xa, `1`/`3` chém ngang xoay mượt nhân vật + camera $90^\circ$ trong `~0.15s`, `2` lướt tiến Forward Dash, `Q`/`E` lăn né trái/phải, `V` đổi góc nhìn, `Enter` mở Chat) giúp người chơi nắm bắt ngay trong 30 giây đầu tiên.
- **Difficulty scaling:** Độ khó tự cân bằng theo trình độ của những người chơi thật trong phòng; càng về cuối trận khi số người sống sót (`AliveCount`) giảm xuống, áp lực tâm lý và sự tập trung càng tăng cao.
- **Feedback clarity:** Thanh máu World-Space Billboard (`100/100`) trên đầu đối thủ, HUD chính và trạng thái Cooldown trên cụm nút kỹ năng góc dưới phải cập nhật tức thời; hiệu ứng vệt chém dọc/ngang $90^\circ$, trúng đòn (`Rpc_PlayAttackEffect`) và âm thanh 3D (`Spatial Blend = 1.0`) cho biết ngay đòn đánh có trúng hay bị vật cản chặn.
- **Recovery from failure:** Khi tử trận (`IsDead = true`), người chơi không bị đẩy ra màn hình chờ tĩnh mà lập tức chuyển sang **Ghost Mode Spectator** để tiếp tục theo dõi trận đấu, trò chuyện với mọi người và sẵn sàng vào ngay ván kế tiếp sau vài phút.

---

## Core Loop

### Moment-to-Moment (30 seconds)
Người chơi điều khiển nhân vật Capsule di chuyển 360° (`WASD`) và xoay hướng theo chuột quanh các khối `Obstacle` trên sàn đấu; bấm phím `V` để đổi giữa góc nhìn **TPS** (quan sát góc khuất) và **FPS** (ngắm bắn chính xác); bấm **Chuột phải (`Right Click`)** (hoặc nút mềm trên màn hình) để bắn đạn tầm xa (**Ranged Attack**), hoặc canh thời điểm đối phương sơ hở để bấm `Q`/`E` (**Side Dodge Roll** với hiệu ứng DOTween Squash & Stretch) né đạn, bấm **phím `2` (`Forward Dash`)** phóng nhanh tới áp sát rồi tung **Chuột trái (`Left Click` — Chém dọc từ trên xuống)** hoặc **Phím `1` / `3` (Chém ngang hình quạt $90^\circ$ sang trái/phải đồng thời xoay thân người $90^\circ$ theo hướng chém)** để kết liễu mục tiêu.

### Short-Term (5-15 minutes)
Một ván đấu **Last Man Standing** hoàn chỉnh (3–5 phút): Người chơi tham gia phòng Shared Mode, giao tranh sinh tồn với các đấu sĩ khác trong khi đồng hồ trận đấu (`MatchTimer` đồng bộ bằng `TickTimer`) đếm ngược. Những người hết máu (`CurrentHealth <= 0`) chuyển sang **Ghost Mode** quan sát và bình luận trên Chat. Ván đấu khép lại khi `AliveCount == 1`, công bố **Người Chiến Thắng (Winner)** và cập nhật bảng điểm.

### Session-Level (30-120 minutes)
Một phiên chơi gồm 5–10 ván đấu liên tiếp: Người chơi thử nghiệm các chiến thuật di chuyển/góc nhìn khác nhau, giao lưu với bạn bè qua hệ thống Chat (Public/Whisper), theo dõi thứ hạng của mình tăng lên trên **PlayFab Leaderboard** sau mỗi chiến thắng và dừng lại tự nhiên sau khi đạt mục tiêu Top bảng xếp hạng.

### Long-Term Progression
Tích lũy thành tích cá nhân bền vững trên đám mây **PlayFab** (`Total Wins`, `Total Kills` trong `Player Data` và thứ hạng trên `PlayFab Leaderboards`), kết hợp rèn luyện kỹ năng phản xạ cá nhân (mastery) qua từng trận đấu.

### Retention Hooks
- **Curiosity:** Muốn thử nghiệm các pha phối hợp kỹ năng mới (Dash + Melee, FPS sniping qua khe hẹp).
- **Investment:** Thành tích `Wins` và `Kills` được lưu trữ trên PlayFab Cloud gắn liền với tên định danh của người chơi.
- **Social:** Trò chuyện công khai hoặc nhắn tin riêng (Whisper) với đối thủ ngay trong trận và khi đang ở chế độ Ghost Mode.
- **Mastery:** Nâng cao kỹ năng đọc chuyển động đối phương, căn Cooldown `TickTimer` và leo lên vị trí Top 1 trên PlayFab Leaderboard.

---

## Game Pillars

### Pillar 1: Cơ Động Phản Xạ Định Đoạt Sinh Tồn (Mobility & Reflex Outplay)
Mọi pha giao tranh đều được quyết định bởi việc đọc tình huống, sử dụng **Forward Dash** và **Side Dodge Roll (`Q/E`)** đúng nhịp Cooldown (`TickTimer`) để né đạn tầm xa hoặc áp sát tung đòn cận chiến, thay vì đứng yên đấu súng chỉ số.

*Design test*: Nếu phải chọn giữa việc tăng tốc độ/sát thương đòn bắn xa để hạ gục tức thì hay giữ tốc độ đạn vừa phải kèm hiệu ứng rõ ràng để đối phương có cửa sổ phản xạ bấm `Q/E` lăn né rồi Dash phản công, trụ cột này bắt buộc chọn **giữ cửa sổ phản xạ lăn né và phản công**.

### Pillar 2: Nhãn Quan Chiến Thuật Kép FPS $\leftrightarrow$ TPS (Dual-Perspective Tactical Mastery)
Góc nhìn thứ ba (**TPS**) và góc nhìn thứ nhất (**FPS**) trên Cinemachine mang lại lợi thế chiến thuật bổ trợ nhau quanh các khối vật cản (`Obstacle`), biến việc chuyển góc nhìn (phím `V`) thành một kỹ năng chiến đấu thực thụ.

*Design test*: Nếu thiết kế bản đồ đấu trường chỉ là mặt phẳng trống trải hoặc đặt các khối `Obstacle` quá thấp không che được tầm nhìn, trụ cột này yêu cầu **bố trí các khối `Obstacle` có chiều cao và khoảng cách hợp lý để TPS phát huy lợi thế quan sát góc khuất và FPS phát huy lợi thế ngắm bắn xuyên khe hẹp**.

### Pillar 3: Công Bằng Mạng Tuyệt Đối Trên Shared Mode (Tick-Accurate Shared Authority)
Trải nghiệm chiến đấu nhiều người chơi phải luôn mượt mà, minh bạch và công bằng thông qua `FixedUpdateNetwork()`, phân quyền `StateAuthority` chuẩn xác, `[Rpc]` sát thương/hiệu ứng và bù trễ mạng (`LagCompensation` với `HitboxRoot`).

*Design test*: Nếu một hiệu ứng kỹ năng hoặc tính toán va chạm chạy trong `Update()` gây lệch kết quả giữa các máy Client, trụ cột này bắt buộc **chuyển toàn bộ logic mô phỏng về `FixedUpdateNetwork()` theo Tick mạng và kiểm tra va chạm/trừ máu qua luồng `StateAuthority` chuẩn xác**.

### Pillar 4: Khán Đài Hồn Ma & Vinh Quang Bảng Vàng (Active Spectating & Competitive Glory)
Bị hạ gục không phải là kết thúc nhàm chán — người chơi tử trận lập tức chuyển sang **Ghost Mode** theo dõi các đấu sĩ còn sống, trò chuyện qua **Public/Private Chat** và chờ đón khoảnh khắc vinh danh Top 1 lưu danh lên **PlayFab Leaderboard**.

*Design test*: Nếu khi `CurrentHealth <= 0` màn hình chỉ hiện bảng "Game Over" tĩnh bắt người chơi thoát phòng, trụ cột này yêu cầu **chuyển ngay Cinemachine sang Ghost Mode bám theo người chơi còn sống kèm nút chuyển mục tiêu (`Next/Previous`) và khung Chat tương tác**.

### Anti-Pillars (What This Game Is NOT)

- **NOT a Pay-to-Win / Stat-Grinding RPG**: Chúng tôi KHÔNG thêm hệ thống nâng cấp vũ khí/giáp cộng máu hay sát thương vĩnh viễn giữa các ván vì nó phá vỡ **Pillar 1 (Cơ Động Phản Xạ)** và tính công bằng của đấu trường sinh tồn.
- **NOT an Open-World Looting Simulator**: Chúng tôi KHÔNG bắt người chơi chạy tìm vũ khí/balo trên bản đồ rộng vì điều đó làm loãng nhịp độ giao tranh tức thì của **Pillar 1 & Pillar 2** và vượt quá phạm vi demo GAM302.
- **NOT an Unverified Client-Mutation Netcode Demo**: Chúng tôi KHÔNG cho phép Client bất kỳ tự ý ghi đè máu của người khác ngoài luồng `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` vì nó vi phạm **Pillar 3 (Công Bằng Mạng Shared Mode)**.
- **NOT a Cluttered Maze Over Readability**: Chúng tôi KHÔNG sử dụng địa hình phức tạp gây kẹt vật lý hoặc che khuất thanh máu/tên nhân vật Billboard trên đầu vì nó làm hỏng **Pillar 2 (Nhãn Quan Chiến Thuật)** và khả năng theo dõi của **Pillar 4 (Ghost Mode)**.

---

## Visual Identity Anchor

> **Selected Direction (`AD-CONCEPT-VISUAL: STRONG`)**: **"Tactical Toybox Arena" (Đấu Trường Đồ Chơi Chiến Thuật)**

- **One-Line Visual Rule:** *"Mọi bề mặt môi trường đều tĩnh lặng và lì (matte), mọi hành động chiến đấu đều đàn hồi và phát sáng."*
- **Supporting Visual Principles & Design Tests:**
  1. **Nhân vật Đàn Hồi & Nổi Bật (Elastic Capsule Readability):** Tận dụng trọn vẹn phom dáng tròn trịa, tươi sáng của gói **Cartoon Capsule Pack Lite** kết hợp biến dạng **DOTween Squash & Stretch** (biên độ 120%–130%) và vệt Trail rõ nét khi `Forward Dash` / `Side Dodge Roll (Q/E)`.
     - *Design Test:* Nếu một hiệu ứng môi trường làm mờ đường viền nhân vật hoặc khiến người chơi không nhận ra đối thủ đang lăn né trong 100ms đầu tiên, phải giảm độ chi tiết môi trường và tăng độ tương phản của Trail/Squash & Stretch.
  2. **Hình Học Chiến Thuật Tương Phản Độ Sáng (High-Contrast Geometric Arena):** Mặt sàn (`Plane`), tường bao (`Boundary`) và các khối vật cản (`Obstacle` Cubes) giữ hình khối chuẩn xác cho va chạm vật lý nhưng có đường lưới (Grid/Edge Trim) để người chơi cảm nhận rõ tốc độ lướt ở TPS và khoảng cách tới mặt tường ở FPS.
     - *Design Test:* Nếu khi chuyển sang góc nhìn FPS sát khối `Obstacle`, người chơi bị mất phương hướng vì bề mặt tường một màu trơn, bắt buộc áp dụng Material có viền chỉ hoặc họa tiết lưới định hướng.
  3. **Giao Diện & Tín Hiệu Chiến Đấu Sắc Nét (Esports-Grade Signal Hierarchy):** Thanh máu World-Space Billboard (`100/100`), tia đạn `Ranged`, vệt chém `Melee` và khung HUD/Chat (`CanvasGroup`) luôn có độ tương phản cao nhất trên màn hình.
     - *Design Test:* Nếu màu của viên đạn hoặc thanh máu trùng với tông màu của sàn đấu hay khối `Obstacle`, bắt buộc đổi màu môi trường về tông trung tính độ bão hòa thấp.
- **Color Philosophy:**
  - **Sàn đấu (`Plane`):** Xám lam trung tính (Desaturated Cool Slate, Value ~45%) có lưới mờ (Grid) giúp định vị khoảng cách Dash và tầm đạn.
  - **Vật cản (`Obstacle` Cubes):** Tông sáng tương phản rõ với sàn (Trắng sứ / Xám sáng viền Cam công nghiệp) để nhận diện góc che chắn (Cover) tức thì ở cả FPS và TPS.
  - **Tường bao (`Boundary`):** Tông tối trầm kèm dải cảnh báo ở chân tường báo hiệu ranh giới đấu trường.
  - **Nhân vật, VFX & UI:** Màu nguyên sắc độ bão hòa cao (High-Saturation Primaries) dành riêng cho nhân vật Cartoon Capsule, đạn bắn xa, hiệu ứng trúng đòn (`Rpc_PlayAttackEffect`) và thanh máu Billboard (Xanh lá tươi $\rightarrow$ Đỏ tươi trên nền khung tối bán trong suốt). Chế độ **Ghost Mode Spectator** phủ viền sắc Xanh Lục Lam (Ethereal Cyan) nhẹ nhàng để báo hiệu trạng thái quan sát.

---

## Inspiration and References

| Reference | What We Take From It | What We Do Differently | Why It Matters |
| ---- | ---- | ---- | ---- |
| **Brawl Stars** | Độ rõ ràng tuyệt đối của thanh máu/tên trên đầu nhân vật (Overhead UI), nhịp độ né đòn – phản công nhanh trong đấu trường nhỏ. | Chuyển từ góc nhìn Top-Down cố định sang không gian 3D hoàn chỉnh với khả năng chuyển đổi góc nhìn **FPS $\leftrightarrow$ TPS** linh hoạt. | Chứng minh vòng lặp chiến đấu ít kỹ năng nhưng giàu tính định vị và quản lý Cooldown có chiều sâu rất lớn. |
| **PUBG / Fortnite** | Cơ chế tận dụng vật cản (`Obstacle`) và chuyển đổi giữa góc nhìn thứ ba (quan sát góc khuất) với góc nhìn thứ nhất (ngắm bắn chính xác) trong chế độ sinh tồn đến người cuối cùng. | Tinh gọn toàn bộ giai đoạn nhảy dù/nhặt đồ thành một đấu trường hộp tức thì với bộ 6 kỹ năng có sẵn ngay khi Spawn. | Khẳng định giá trị chiến thuật thực tế của việc kết hợp Cinemachine FPS và TPS trong cùng một trận đấu. |
| **Fall Guys** | Tạo hình nhân vật Capsule tươi sáng, hiệu ứng đàn hồi (Squash & Stretch) vui nhộn và trải nghiệm **Spectator Mode** hào hứng sau khi bị loại. | Tập trung vào đối kháng kỹ năng chiến đấu (Melee/Ranged/Dash/Dodge Roll) và tích hợp **In-Game Chat (Public/Whisper)** cùng **PlayFab Leaderboard**. | Minh họa cách giữ chân người chơi ở lại phòng sau khi bị hạ gục thông qua trải nghiệm quan sát xã hội. |

**Non-game inspirations**: Các giải đấu thể thao điện tử thu nhỏ (Arena Esports Showdowns) và dòng đồ chơi mô hình Designer Vinyl Toys — sạch sẽ, sắc nét, hiện đại và giàu tính tương tác.

---

## Target Player Profile

| Attribute | Detail |
| ---- | ---- |
| **Age range** | 18–30 (Sinh viên ngành lập trình game, giảng viên/hội đồng chấm thi GAM302 và người chơi yêu thích dòng game Arena PvP) |
| **Gaming experience** | Mid-core đến Hardcore (quen thuộc với thao tác bàn phím `WASD` + chuột trên PC) |
| **Time availability** | Phiên chơi ngắn 15–30 phút (3–5 phút mỗi ván đấu) |
| **Platform preference** | PC (Windows Standalone) |
| **Current games they play** | *Brawl Stars*, *Valorant*, *Overwatch 2*, *PUBG*, *Fall Guys* |
| **What they're looking for** | Một trận đấu nhiều người chơi kết nối nhanh, điều khiển nhạy, mạng đồng bộ chuẩn xác không giật lag và có bảng xếp hạng cạnh tranh rõ ràng. |
| **What would turn them away** | Đòn đánh trúng trên màn hình nhưng không mất máu do lệch mạng, kẹt nhân vật vào tường, hoặc bị khóa phím di chuyển lỗi khi đang gõ Chat. |

---

## Technical Considerations

| Consideration | Assessment |
| ---- | ---- |
| **Recommended Engine** | **Unity 6.6 (`6000.6.8f1`, URP)** — Khớp hoàn toàn với bộ công cụ thực hành môn GAM302, hỗ trợ tốt Cinemachine 3.x, Input System (JSON Rebinding), DOTween, Photon Fusion 2 và PlayFab SDK. |
| **Key Technical Challenges** | 1) Đồng bộ chuyển động Dash (phím `2`), Dodge Roll (`Q`/`E`) và phép xoay thân người $90^\circ$ sau đòn chém ngang (phím `1`/`3`) mượt mà trong `FixedUpdateNetwork()` dưới quyền `StateAuthority`; 2) Kết hợp `HitboxRoot` (`LagCompensation`) và Physics Cast hình quạt dọc/ngang $90^\circ$ + `[Rpc]` trừ máu chuẩn xác trong Shared Mode; 3) Kiến trúc cấu hình dữ liệu qua `ScriptableObject` (`CharacterCombatConfigSO`), lưu/nạp Keybinding qua JSON + UI Rebinding và cụm On-Screen Skill Buttons chuẩn bị cho Mobile; 4) Chuyển giao quyền `SharedModeMasterClient` cho `MatchManager` và khóa/mở Input Focus an toàn giữa chiến đấu, khung Chat và Ghost Mode. |
| **Art Style** | **3D Stylized ("Tactical Toybox Arena")** — Nhân vật từ gói **Cartoon Capsule Pack Lite** kết hợp đấu trường hình học URP sạch sẽ, tương phản cao. |
| **Art Pipeline Complexity** | **Low** — Tận dụng prefab nhân vật có sẵn từ Asset Store và các khối 3D cơ bản (`Plane`, `Cube`) kèm URP Materials và DOTween. |
| **Audio Needs** | **Moderate (3D Spatial Audio)** — Sử dụng `AudioSource` với `Spatial Blend = 1.0` và `Min/Max Distance` hợp lý, đồng bộ qua `[Rpc]` cho tiếng bước chân, chém dọc/chém ngang, bắn đạn và trúng đòn. |
| **Networking** | **Photon Fusion 2 — Shared Mode (`GameMode.Shared`)** — Quản lý trạng thái qua Photon Cloud (`AppId: fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`), phân quyền `StateAuthority` trên từng Client kết hợp `INetworkObjectProvider` Object Pooling và PlayFab Cloud Backend. |
| **Content Volume** | **1 đấu trường 3D hoàn chỉnh**, 1 bộ nhân vật Cartoon Capsule (đọc chỉ số từ `ScriptableObject`), hệ thống chiến đấu đa hướng (Dash `2`, Dodge `Q`/`E`, Chém dọc `LMB`, Chém ngang xoay $90^\circ$ `1`/`3`, Bắn xa `RMB`) kèm cụm nút bấm mềm góc dưới phải, hệ thống Keybinding JSON/UI, 2 chế độ Camera + Ghost Mode, hệ thống Chat Public/Whisper và PlayFab Leaderboard. |
| **Procedural Systems** | **None** — Đấu trường được bố trí cố định có chủ đích chiến thuật; vị trí Spawn của người chơi được chọn từ danh sách các điểm `SpawnPoint` an toàn quanh đấu trường. |

---

## Risks and Open Questions

### Design Risks
- **Mất cân bằng giữa đánh xa (`Ranged`) và đánh gần (`Melee`):** Nếu đạn bắn xa có tốc độ quá nhanh và hồi chiêu quá ngắn, người chơi sẽ không có động lực dùng `Forward Dash` để áp sát chém cận chiến. *Giảm thiểu:* Giới hạn tầm bay/tốc độ đạn và thiết kế `Melee Attack` có sát thương cao hơn hẳn, kết hợp các khối `Obstacle` che chắn đường đạn.
- **Lợi thế một chiều giữa TPS và FPS:** Người chơi có thể chỉ dùng TPS vì tầm nhìn rộng hơn. *Giảm thiểu:* Thiết kế góc nhìn FPS có tâm ngắm (Crosshair) chính xác tuyệt đối khi bắn tầm xa qua các khe hẹp giữa các khối `Obstacle`.

### Technical Risks (`TD-FEASIBILITY: VIABLE`)
- **Đồng bộ chuyển động tốc độ cao (Dash / Dodge Roll) trong Shared Mode:** Can thiệp sai vào `transform.position` hoặc xử lý ngoài `FixedUpdateNetwork()` sẽ gây giật cục (rubber-banding) hoặc xuyên tường. *Giảm thiểu:* Sử dụng `CharacterController` / `NetworkTransform` (hoặc `NetworkRigidbody3D` với `ContinuousDynamic`) thực thi hoàn toàn trong `FixedUpdateNetwork()` khi `Object.HasStateAuthority == true`, quản lý thời gian bằng `[Networked] TickTimer`.
- **Lag Compensation (`HitboxRoot`) & chống trừ máu kép trong Shared Mode:** Trong `GameMode.Shared`, mỗi Client nắm `StateAuthority` của nhân vật mình nên việc phát hiện va chạm cần kết hợp chuẩn xác giữa `HitboxRoot` / `Runner.LagCompensation` và Physics Cast theo Layer, đồng thời viên đạn từ Object Pool phải có cờ `_hasDealtDamage` để chỉ gửi duy nhất 1 lệnh `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` (`Rpc_TakeDamage`) trước khi `Runner.Despawn()`, và bên nhận kiểm tra `if (IsDead || CurrentHealth <= 0) return;`.
- **Rời phòng của `SharedModeMasterClient`:** Nếu máy giữ quyền điều khiển `MatchManager` thoát giữa trận, bộ đếm thời gian và xét thắng thua có thể bị gián đoạn. *Giảm thiểu:* Cấu hình `MatchManager` là Scene `NetworkObject` bật `Allow State Authority Override`, tắt `Destroy When State Authority Leaves`, dùng `[Networked] TickTimer MatchTimer` và tính `AliveCount` động từ danh sách người chơi còn hoạt động trong phòng.
- **Tích hợp PlayFab bất đồng bộ & Xung đột Input khi gõ Chat:** Gọi API PlayFab từ mọi máy hoặc quên khóa phím khi mở Chat sẽ gây lỗi dữ liệu và làm nhân vật tự di chuyển khi đang gõ chữ. *Giảm thiểu:* Bật `Allow client to post player statistics` trên PlayFab, chỉ máy có `HasStateAuthority` mới gửi thống kê của chính mình, và dùng `InputFocusManager.IsChatFocused` để khóa toàn bộ input chiến đấu/camera khi khung Chat (`CanvasGroup`) đang mở.

### Market Risks
- **Không áp dụng rủi ro thương mại:** Đây là dự án demo học thuật phục vụ môn **GAM302**, tiêu chí thành công là đáp ứng trọn vẹn, mượt mà và thuyết phục toàn bộ yêu cầu kỹ thuật trong đặc tả dự án.

### Scope Risks (`PR-SCOPE: REALISTIC`)
- **Tích hợp nhiều hệ thống mạng trong 3–4 tuần của Solo Developer:** Dự án bao gồm cả Fusion 2 Shared Mode, 6 kỹ năng, Cinemachine FPS/TPS/Ghost Mode, Chat và PlayFab. *Giảm thiểu:* Giữ khối lượng tài nguyên 3D ở mức tối thiểu (1 đấu trường hình học + gói `Cartoon Capsule Pack Lite` có sẵn) và chia thành 4 tầng phạm vi (Scope Tiers) rõ ràng theo từng tuần.

### Open Questions
- **Thông số Tick Rate & Cooldown tối ưu:** Cần kiểm thử thực tế giữa Tick Rate `30Hz` và `60Hz` trong `NetworkProjectConfig` cho `GameMode.Shared` để chọn bộ thông số tốc độ Dash/Dodge Roll và tốc độ bay của viên đạn mượt nhất.

---

## MVP Definition

**Core hypothesis**: Một đấu trường 3D nhiều người chơi hoạt động trên **Photon Fusion 2 — Shared Mode (`GameMode.Shared`)** với bộ 6 kỹ năng điều khiển/chiến đấu theo Tick mạng, chuyển đổi góc nhìn **FPS $\leftrightarrow$ TPS** và chế độ **Ghost Mode Spectator** mang lại trải nghiệm đối kháng sinh tồn công bằng, nhạy bén và hấp dẫn.

**Required for MVP**:
1. **Kết nối Shared Mode & Khởi tạo Nhân vật (Data-Driven `ScriptableObject`):** Màn hình Lobby nhập Tên nhân vật + chọn Region, kết nối `GameMode.Shared` và `Runner.Spawn()` nhân vật `Cartoon Capsule` với `StateAuthority` chuẩn xác; toàn bộ chỉ số tốc độ di chuyển, tốc độ lướt, sát thương và tầm đánh được đọc từ `ScriptableObject` (`CharacterCombatConfigSO`).
2. **Đấu trường 3D, Bộ Kỹ năng Đa hướng trong `FixedUpdateNetwork()` & Hệ thống Keybinding / Nút bấm mềm:**
   - Mặt sàn `Plane`, tường bao `Boundary`, vật cản `Obstacle`.
   - Di chuyển `WASD` 360°, xoay theo chuột, **Forward Dash (phím `2`, `TickTimer`)**, **Side Dodge Roll (`Q`/`E`)**, **Chém dọc (`Left Click` — hình quạt thẳng trước mặt từ trên xuống)**, **Chém ngang xoay người $90^\circ$ (phím `1` sang trái / phím `3` sang phải — quét quạt $90^\circ$ rồi xoay hướng nhân vật $90^\circ$)**, và **Bắn đạn tầm xa (`Right Click`)**.
   - Cụm **Button mềm góc dưới phải màn hình** (sẵn sàng cho Mobile) hiển thị đầy đủ các chiêu thức và liên kết với hệ thống **Keybinding (JSON + In-Game Binding UI)**.
3. **Đồng bộ Máu (`[Networked]` + `[OnChangedRender]`) & RPC Sát thương:** Thanh máu World-Space Billboard (`100/100`) trên đầu nhân vật + HUD chính, trừ máu qua `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`.
4. **Camera `FPS <-> TPS`, `Ghost Mode Spectator` & Điều kiện thắng `Last Man Standing`:** Đổi góc nhìn bằng phím `V` (đồng bộ góc nhìn khi nhân vật xoay $90^\circ$ sau đòn chém ngang phím `1`/`3`), chuyển sang theo dõi người còn sống khi `IsDead == true` và công bố Winner khi `AliveCount == 1`.

**Explicitly NOT in MVP** (defer to Tier 3 & Tier 4):
- Hệ thống In-Game Chat (Public & Private Whisper với `CanvasGroup`).
- Tích hợp đám mây PlayFab (Login, Player Data / Title Data, Leaderboard `Wins` / `Kills`).
- Hiệu ứng đánh bóng nâng cao (DOTween Squash & Stretch chi tiết, 3D Spatial Audio đầy đủ).

### Scope Tiers (if budget/time shrinks)

| Tier | Content | Features | Timeline |
| ---- | ---- | ---- | ---- |
| **MVP** | 1 Đấu trường 3D (`Plane`, `Boundary`, `Obstacle`) + 1 Prefab `Cartoon Capsule` + `CharacterCombatConfigSO` | Kết nối `GameMode.Shared` (Tên + Region), Spawn nhân vật, bộ kỹ năng trong `FixedUpdateNetwork()` (WASD, Dash `2`, Dodge `Q`/`E`, Chém dọc `LMB`, Chém ngang xoay $90^\circ$ `1`/`3`, Bắn xa `RMB`), cụm Button mềm góc dưới phải + cấu hình Keybinding JSON, trừ máu qua `[Rpc]`, Overhead Billboard UI & HUD máu cơ bản. | **Tuần 1** |
| **Vertical Slice** | Hoàn thiện vòng lặp trận đấu, UI Rebinding & Camera trên đấu trường chính | Camera Cinemachine `FPS <-> TPS` (phím `V`), `Ghost Mode Spectator` (Next/Prev), `MatchManager` (`SharedModeMasterClient`) đếm `MatchTimer` & công bố Winner (`AliveCount == 1`), giao diện gán phím trực tiếp trong game (Keybinding UI), `HitboxRoot` Lag Compensation & `INetworkObjectProvider` Object Pooling cho đạn (`Right Click`). | **Tuần 2** |
| **Alpha** | Đầy đủ tính năng âm thanh, giao tiếp & đám mây theo đặc tả GAM302 | Đồng bộ 3D Spatial Audio (`Spatial Blend = 1.0`) qua `[Rpc]`, hệ thống In-Game Chat (Public & Whisper bằng `CanvasGroup` + khóa Input), tích hợp PlayFab SDK (Login, Player Data/Title Data, gửi và hiển thị Leaderboard `Wins`/`Kills`). | **Tuần 3** |
| **Full Vision** | Bản dựng hoàn chỉnh, đánh bóng trải nghiệm & tối ưu hóa mạng/vật lý | Hiệu ứng DOTween Squash & Stretch cho Dash/Dodge Roll/Slash, VFX trúng đòn (`Rpc_PlayAttackEffect`), tối ưu Physics Layer Matrix (`Player`, `Projectile`, `Obstacle`, `Boundary`), tinh chỉnh thông số trên `ScriptableObject` và kiểm thử đa Client. | **Tuần 4** |

---

## Next Steps

- [x] Get concept approval from creative-director (`CD-PILLARS: APPROVE`)
- [ ] Fill in CLAUDE.md technology stack based on engine choice (`/setup-engine`)
- [x] Create Art Bible (`/art-bible` — `AD-ART-BIBLE: APPROVE`)
- [ ] Validate concept document completeness (`/design-review design/gdd/game-concept.md`)
- [ ] **Prototype core idea** (`/prototype`) — validate Shared Mode movement & combat feel
- [x] Decompose concept into systems (`/map-systems` — `TD-SYSTEM-BOUNDARY: APPROVE`, `PR-SCOPE: REALISTIC`, `CD-SYSTEMS: APPROVE`)
- [ ] Design each system (`/design-system [system-name]`)
- [ ] Author architecture blueprint & ADRs (`/create-architecture`, `/architecture-decision`)
- [ ] Build vertical slice in Pre-Production (`/vertical-slice`)
- [ ] Validate core loop with playtest (`/playtest-report`)
- [ ] Plan first milestone (`/sprint-plan`)
