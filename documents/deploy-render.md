# Deploy TaskFlow: Render + MonsterASP.NET SQL Server

คู่มือนี้ใช้ Render รัน Linux Docker image และเชื่อม SQL Server ของ MonsterASP.NET
ไม่ใช้ PostgreSQL และไม่ส่ง Connection String หรือรหัสผ่านขึ้น GitHub
Demo URL: [เปิด TaskFlow](https://task-managements-11n6.onrender.com/)
ไฟล์ใน repository ไม่สร้างหรือเปลี่ยนบริการ Cloud ด้วยตัวเอง
หลัง deploy ยังต้องตรวจการใช้งานกับ DB จริงและ key persistence ตามขั้นตอนท้ายคู่มือ

## 1. สร้างฐานข้อมูล

ใน MonsterASP.NET Control Panel เลือก Databases → Add database → FREE → SQL Server
Free DB อยู่ใน EU จึงควรเลือก Render region ใกล้กันจากตัวเลือกที่เปิดให้ใช้
Collation เริ่มต้นใช้ได้; `Thai_CI_AS` ก็ใช้ได้ถ้าต้องการเรียงข้อความตามภาษาไทย
User language ใช้ English โดยภาษาไทยของแอปยังเก็บด้วย `nvarchar`
ดู [วิธีสร้าง DB และข้อจำกัด Free](https://help.monsterasp.net/databases/mssql/create-database)

ตรวจชื่อ DB จริงจาก Control Panel ก่อนรัน SQL
ชื่อที่ใช้แสดงบนหน้าเว็บอาจไม่ใช่ Database name ที่ต้องใส่ใน Connection String

## 2. สร้างและรัน schema

รันจากราก repository เพื่อสร้าง SQL ตาม migrations ปัจจุบัน:

```powershell
dotnet ef migrations script --idempotent --project src/TaskManager.Infrastructure --startup-project src/TaskManager.WebUI --configuration Debug --output artifacts/deploy/taskflow-schema.sql -- --environment Development
```

คำสั่งนี้สร้างไฟล์เท่านั้น ไม่เชื่อมต่อหรือเปลี่ยนฐานข้อมูล
Development ใช้สำหรับโหลดค่าประกอบการสร้าง script ไม่ใช่ปลายทางที่จะรัน SQL
โฟลเดอร์ `artifacts/` ถูก ignore; ควรสร้าง script ใหม่จาก commit ที่จะ deploy ทุกครั้ง

สำหรับ DB ใหม่ ให้เปิด `artifacts/deploy/taskflow-schema.sql` และนำข้อความทั้งหมด
ไปแทนตัวอย่างในหน้า Run T-SQL ของ **DB Cloud ที่ตรวจชื่อแล้ว**
ไม่ต้องเลือก Close active connections first แล้วกด Execute
หาก DB มีข้อมูลอยู่แล้วให้สำรองข้อมูลก่อนและตรวจ migration history
Idempotent หมายถึงข้าม migrations ที่มีใน `__EFMigrationsHistory` ไม่ใช่แก้ schema ที่ต่างกันเองได้

หน้า [Run T-SQL](https://help.monsterasp.net/databases/mssql/run-tsql) รองรับ `GO`
แต่จำกัดเวลา 30 วินาที หากมี timeout/error อย่าถือว่ารันสำเร็จ
ตรวจผลก่อนลองใหม่ และใช้ [Import SQL](https://help.monsterasp.net/databases/mssql/import-sql-file)
หรือ SSMS ถ้าจำเป็น

ตรวจผลโดยรัน:

```sql
SELECT name FROM sys.tables ORDER BY name;
SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
```

ต้องมี `anonymous_users`, `boards`, `board_columns`, `board_participants`,
`task_items`, `data_protection_keys` และ `__EFMigrationsHistory`
พร้อม history ของ InitialCreate และ PersistDataProtectionKeys
ไม่ต้อง seed เอง: กดทดลองใช้งานบน Landing แล้วแอปจะสร้าง guest/board/sample task

## 3. เปิด Remote Access และเก็บ credentials

เข้า DB → Users → Remote access → Enabled แล้วใช้ **Remote access connection string**
จาก Control Panel ซึ่งใช้ public hostname; อย่าใช้ hostname สำหรับ local hosting
ดู [Remote Access](https://help.monsterasp.net/databases/mssql/remote-access)

ใช้ SQL authentication ไม่ใช่ `Trusted_Connection=True`
ควรแยกบัญชี runtime ของแอปจากบัญชีที่ใช้ migrations และให้สิทธิ์อ่าน/เขียน
ตารางแอปและ `data_protection_keys` ตามที่ Control Panel รองรับ
ดู [Database users](https://help.monsterasp.net/databases/mssql/users)

MonsterASP.NET ระบุให้ remote connection ใช้ `Encrypt=True;TrustServerCertificate=True`
เนื่องจาก certificate ไม่ได้ออกโดย public CA
การเชื่อมต่อยังเข้ารหัส แต่ **ไม่ได้ตรวจสอบตัวตน certificate ของ server**
จึงเป็นข้อจำกัดที่ยอมรับสำหรับ demo นี้ ไม่เทียบเท่า TLS ที่ตรวจสอบ certificate ครบ
อย่าปิด Encrypt และอย่าเก็บข้อมูลสำคัญ
ดู [ข้อกำหนด TLS ของผู้ให้บริการ](https://help.monsterasp.net/databases/mssql/secure-connection)

ตั้งค่า IP restrictions ให้แคบที่สุดที่ Control Panel รองรับ
ใช้ outbound ranges ของ Render service ไม่ใช่ IP จากชื่อเว็บ
ดู [Render outbound IPs](https://render.com/docs/outbound-ip-addresses)

## 4. สร้าง Render Web Service

หลังตรวจไฟล์และ push commit ด้วยตนเอง ให้เลือก New → Blueprint และ repository นี้
ใช้ `render.yaml` ที่ราก repository ซึ่งสร้างเฉพาะ Free Docker Web Service
ไม่สร้าง DB หรือ paid disk และปิด auto-deploy ไว้เพื่อควบคุมลำดับ migrations
ชื่อ service ต้องไม่ชนบริการเดิมที่ไม่ต้องการเปลี่ยน

ใส่ `ConnectionStrings__DefaultConnection` ใน Render โดยใช้ Remote connection string
ที่คัดลอกมาจาก MonsterASP.NET **อย่าส่งค่าจริงในแชตหรือ commit ลงไฟล์**
ถ้าสร้าง Web Service แบบ manual ให้ใช้:

| Setting | Value |
| --- | --- |
| Runtime | Docker |
| Dockerfile Path | `src/TaskManager.WebUI/Dockerfile` |
| Docker Context | `.` |
| Root Directory | เว้นว่าง |
| Docker Command | เว้นว่าง |
| Health Check | `/health` |
| Instances | 1 |

Environment ตาม Blueprint:

```text
ASPNETCORE_ENVIRONMENT=Production
PORT=8080
ASPNETCORE_HTTP_PORTS=8080
ASPNETCORE_HTTPS_PORT=443
Hosting__BehindReverseProxy=true
DataProtection__PersistKeysToDatabase=true
ConnectionStrings__DefaultConnection=<ใส่เฉพาะใน Render>
```

เปิด BehindReverseProxy เฉพาะเมื่อ proxy ควบคุม X-Forwarded-Proto และ Container
ไม่รับ traffic สาธารณะโดยตรง แอปรับเฉพาะ scheme ไม่รับ forwarded IP หรือ Host
Render จัดการ TLS ให้ จึงไม่ต้องส่ง localhost certificate ขึ้น Cloud

แอปไม่รัน migrations อัตโนมัติ: ต้องรัน SQL ให้สำเร็จก่อน deploy ทุกครั้งที่ schema เปลี่ยน
Free Render ไม่มี pre-deploy command จึงใช้ขั้นตอน manual นี้
ดู [Render Blueprint](https://render.com/docs/blueprint-spec) และ
[Deploy steps](https://render.com/docs/deploys)

## 5. ตรวจหลัง deploy

- `/health` ตอบ 200: เป็น **liveness เท่านั้น ไม่ได้ยืนยันว่า DB เชื่อมต่อได้**
- เปิด Landing กดทดลองใช้งาน และตรวจว่า guest/board ถูกบันทึกจริง
- เพิ่ม แก้ไข ลบ และลาก Task; แชร์อีก browser เพื่อตรวจ real-time
- ตรวจสิทธิ์ Reset ของ owner และ guest ที่เข้าร่วม
- redeploy แล้วลองส่งฟอร์มเดิมที่ยังไม่หมดอายุ เพื่อยืนยัน key persistence
- ตรวจ Logs ว่าไม่มี SQL, migration, TLS หรือ Data Protection errors

Data Protection keys เก็บใน `data_protection_keys` โดยใช้ชื่อแอปคงที่ TaskFlow
XML keys ยังไม่ได้ห่อเข้ารหัสด้วย certificate/Key Vault; จำกัดสิทธิ์ DB และ backups
การตั้งค่านี้ไม่ใช่การเข้ารหัส Guest/Owner Cookie ซึ่งเป็น raw token ตามเดิม
ดู [Microsoft Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)

Render Free อาจพักเมื่อไม่มี traffic และไฟล์ใน Container ไม่ใช่ persistent storage
ดู [ข้อจำกัด Free](https://render.com/docs/free)
Free hosting ใช้สำหรับ portfolio/demo เท่านั้น ไม่รับประกันบริการหรือข้อมูลถาวร

## ทดสอบ image โดยไม่เชื่อม DB

```powershell
docker build -f src/TaskManager.WebUI/Dockerfile -t taskflow:local .
pwsh -File tests/docker-smoke.ps1
pwsh -File tests/docker-smoke.ps1 -RenderProxy
```

ชุดทดสอบนี้ปิด cleanup และไม่เชื่อม DB จึงยังไม่ยืนยัน CRUD/SQL key persistence บน Cloud

[กลับ README](../README.md)
