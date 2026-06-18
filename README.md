 # Order Management API (Indonesian)

 Prototype REST API untuk manajemen order, dibangun dengan **ASP.NET Core (.NET 10)** sebagai fondasi untuk rewrite sistem distribusi.

 ## Ringkasan Masalah

 Layanan ini dirancang untuk mengatasi:

 - Duplikasi order akibat user menekan tombol berkali-kali
 - Inventory jadi negatif saat ada pesanan concurrent
 - Update status order tidak konsisten dari beberapa admin
 - Sulit tracing operasi karena minim logging

 ## Teknologi Utama

 - **Runtime:** .NET 10 — SDK modern untuk API minimal
 - **Database:** PostgreSQL 17 — ACID, MVCC, dukungan concurrency (`xmin`) yang diperlukan untuk skenario ini
 - **ORM:** EF Core 10 — migrations, API typed, provider Npgsql
 - **Logging:** Serilog — logging terstruktur dengan enrichment correlation ID

 ## Cara Menjalankan (Quick Start)

 ### Pra-syarat

 - .NET 10 SDK
 - Docker (untuk PostgreSQL) atau PostgreSQL yang berjalan di host

 ### Menjalankan PostgreSQL

 ```bash
docker compose up -d
```

 ### Menjalankan API

 ```bash
dotnet run --project src/OrderManagement.Api
```

 Aplikasi akan menjalankan migration dan men-seed produk contoh pada startup.

 Health: `GET /health`

 OpenAPI (hanya di Development): `/openapi/v1.json`

 ### Menjalankan Test

 ```bash
dotnet test
```

 - Tes integrasi menggunakan PostgreSQL pada `localhost:5432` secara default.
 - Opsional: set `USE_TESTCONTAINERS=true` untuk menggunakan Testcontainers (butuh Docker).
 - Opsional: set `TEST_DATABASE_CONNECTION_STRING` untuk menargetkan database lain.

 ## Endpoint API

 - `POST /api/orders` — Buat order (wajib header `Idempotency-Key`)
 - `GET /api/orders/{id}` — Ambil order by ID
 - `GET /api/orders` — List orders dengan filter & pagination
 - `PATCH /api/orders/{id}/status` — Update status (wajib header `If-Match`)
 - `POST /api/orders/{id}/cancel` — Cancel order dan kembalikan stock (wajib `If-Match`)

 ### Contoh Create Order

 ```http
POST /api/orders
Idempotency-Key: order-abc-123
Content-Type: application/json

{
  "customerId": "22222222-2222-2222-2222-222222222201",
  "shippingAddress": "Jl. Contoh No. 123, Bandung",
  "items": [
    { "productId": "11111111-1111-1111-1111-111111111101", "quantity": 2 }
  ]
}
```

 ### Transisi Status

 - `Pending` → `Confirmed` | `Cancelled`
 - `Confirmed` → `Shipped` | `Cancelled`
 - `Shipped` → `Delivered`
 - `Delivered` / `Cancelled` → state terminal (tidak bisa diubah lagi)

 Cancel hanya boleh dilakukan saat status masih `Pending` atau `Confirmed`.

 ### Format Error

 Semua error mengikuti payload konsisten:

 ```json
{
  "errorCode": "CONFLICT",
  "message": "Order was modified by another request. Refresh and retry.",
  "correlationId": "..."
}
```

 ## Keputusan Desain (Singkat)

 ### 1) Idempotency

 - **Pendekatan:** header `Idempotency-Key` + tabel `IdempotencyRecords` + hash payload (`RequestHasher`).
 - Alasan: kunci eksplisit memberi kontrol klien dan mudah dijelaskan; kombinasi key+hash mencegah reuse key untuk payload berbeda.

 ### 2) Concurrency (Fokus)

 - **Scenario A (Deduction stock concurrent):** menggunakan UPDATE kondisional atomik via EF Core `ExecuteUpdate`:

 ```sql
UPDATE "Products"
SET "StockQuantity" = "StockQuantity" - @quantity
WHERE "Id" = @productId AND "StockQuantity" >= @quantity
```

  Sehingga stok tidak pernah negatif dan hanya request yang memenuhi kondisi yang akan mengurangi stok.

 - **Scenario B (Status update concurrent):** optimistic concurrency menggunakan PostgreSQL `xmin` dipetakan ke `Order.RowVersion`. Klien harus menyertakan header `If-Match` dengan row version; konflik menghasilkan `409 Conflict`.

 - **Scenario C (Idempotent create under race):** unique index pada `IdempotencyRecords.Key` + transactional flow + penanganan unique-violation sehingga hanya satu order dibuat untuk key yang sama.

 ### 3) Race Conditions Tambahan & Mitigasi

 - Double restore saat concurrent cancel: dicegah dengan `If-Match` + pengecekan transisi status + transaksi DB.
 - Partial deduction pada multi-item order: seluruh operasi create dijalankan dalam satu transaksi; kegagalan me-rollback semua perubahan.

 ## Logging & Tracing

 - Serilog terpasang dan `CorrelationIdMiddleware` menambahkan `X-Correlation-Id` untuk trace antar log.

 ## Produk yang di-seed (sample)

 - Product X — `11111111-1111-1111-1111-111111111101` — stock 15 — price 100.00
 - Product Y — `11111111-1111-1111-1111-111111111102` — stock 50 — price 25.50
 - Product Z — `11111111-1111-1111-1111-111111111103` — stock 100 — price 10.00

 ## Struktur Proyek

 ```
src/OrderManagement.Core/
  Domain/
  Data/
  Services/
  Contracts/
src/OrderManagement.Api/
  Endpoints/
  Middleware/
tests/OrderManagement.Tests/
  Infrastructure/
```

 ## Catatan untuk Presentasi

 - PostgreSQL dipilih karena perilaku concurrency yang mendekati production (MVCC, row-level visibility).
 - Idempotency key bergaya industri (client-controlled) memudahkan retry yang aman.
 - Optimistic locking memberikan konflik deterministik tanpa menurunkan throughput baca.

 ## Hasil Test

 Semua test unit dan integrasi berjalan dan lulus pada lingkungan pengujian: `20` tests berhasil (termasuk tes concurrent untuk skenario A, B, dan C).

