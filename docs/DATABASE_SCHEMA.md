# مخطط قاعدة البيانات - نظام ERP

## 📊 نظرة عامة

قاعدة البيانات تستخدم **SQL Server** مع **Entity Framework Core** كـ ORM. جميع الجداول تستخدم أسماء ASCII مع دعم كامل للعربية في البيانات.

---

## 🗂️ تصنيف الجداول

### 1. المبيعات (Sales)

#### SalesInvoices
فاتورة البيع الرئيسية

| العمود | النوع | الوصف |
|--------|------|-------|
| SIId | int (PK) | رقم الفاتورة |
| CustomerId | int (FK) | العميل |
| WarehouseId | int (FK) | المخزن |
| SIDate | date | تاريخ الفاتورة |
| SITime | time | وقت الفاتورة |
| Status | nvarchar(20) | الحالة (مسودة/مرحل/ملغى) |
| IsPosted | bit | هل مُرحّلة؟ |
| NetTotal | decimal(18,2) | الإجمالي الصافي |

**العلاقات**:
- `CustomerId` → `Customers.CustomerId`
- `WarehouseId` → `Warehouses.WarehouseId`
- One-to-Many مع `SalesInvoiceLines`

#### SalesInvoiceLines
سطور فاتورة البيع

| العمود | النوع | الوصف |
|--------|------|-------|
| SIId | int (PK, FK) | رقم الفاتورة |
| LineNo | int (PK) | رقم السطر |
| ProdId | nvarchar(50) (FK) | الصنف |
| Qty | decimal(18,4) | الكمية |
| PriceRetail | decimal(18,2) | سعر الجمهور |
| UnitSalePrice | decimal(18,2) | سعر البيع للوحدة |
| LineNetTotal | decimal(18,2) | صافي السطر |

**العلاقات**:
- `(SIId, LineNo)` → `SalesInvoices.SIId`
- `ProdId` → `Products.ProdId`

---

### 2. المشتريات (Purchasing)

#### PurchaseInvoices
فاتورة الشراء الرئيسية

| العمود | النوع | الوصف |
|--------|------|-------|
| PIId | int (PK) | رقم الفاتورة |
| CustomerId | int (FK) | المورد |
| WarehouseId | int (FK) | المخزن |
| Status | nvarchar(20) | الحالة |
| IsPosted | bit | هل مُرحّلة؟ |
| RefPRId | int (FK, nullable) | طلب الشراء المرجعي |

**العلاقات**:
- `CustomerId` → `Customers.CustomerId`
- `WarehouseId` → `Warehouses.WarehouseId`
- `RefPRId` → `PurchaseRequests.PRId`
- One-to-Many مع `PILines`

#### PILines
سطور فاتورة الشراء

| العمود | النوع | الوصف |
|--------|------|-------|
| PIId | int (PK, FK) | رقم الفاتورة |
| LineNo | int (PK) | رقم السطر |
| ProdId | nvarchar(50) (FK) | الصنف |
| BatchNo | nvarchar(50) | رقم التشغيلة |
| Expiry | date | تاريخ الصلاحية |
| Qty | decimal(18,4) | الكمية |
| UnitCost | decimal(18,4) | تكلفة الوحدة |
| PriceRetail | decimal(18,2) | سعر الجمهور |
| PurchaseDiscountPct | decimal(5,2) | خصم الشراء % |

**العلاقات**:
- `(PIId, LineNo)` → `PurchaseInvoices.PIId`
- `ProdId` → `Products.ProdId`

---

### 3. المخزون (Stock)

#### StockLedger
دفتر الحركة المخزني (مصدر الحقيقة)

| العمود | النوع | الوصف |
|--------|------|-------|
| EntryId | int (PK) | رقم القيد |
| WarehouseId | int (FK) | المخزن |
| ProdId | nvarchar(50) (FK) | الصنف |
| BatchId | int (FK, nullable) | التشغيلة |
| BatchNo | nvarchar(50) | رقم التشغيلة |
| Expiry | date | تاريخ الصلاحية |
| TranDate | datetime2 | تاريخ الحركة |
| QtyIn | decimal(18,4) | كمية الدخول |
| QtyOut | decimal(18,4) | كمية الخروج |
| RemainingQty | decimal(18,4) | الكمية المتبقية (FIFO) |
| UnitCost | decimal(18,4) | تكلفة الوحدة |
| TotalCost | decimal(18,2) | إجمالي التكلفة |
| PriceRetail | decimal(18,2) | سعر الجمهور |
| PurchaseDiscountPct | decimal(5,2) | خصم الشراء % |
| SourceType | nvarchar(30) | نوع المصدر |
| SourceId | int | رقم المستند |
| SourceLine | int | رقم السطر |

**العلاقات**:
- `WarehouseId` → `Warehouses.WarehouseId`
- `ProdId` → `Products.ProdId`
- `BatchId` → `Batches.Id`

**الفهارس**:
- `(WarehouseId, ProdId)`
- `(WarehouseId, ProdId, Expiry, TranDate, EntryId)` - للـ FIFO
- `(SourceType, SourceId, SourceLine)`

#### StockFifoMap
ربط المخارج بالمداخل (FIFO)

| العمود | النوع | الوصف |
|--------|------|-------|
| MapId | int (PK) | رقم الربط |
| OutEntryId | int (FK) | قيد الخروج |
| InEntryId | int (FK) | قيد الدخول |
| Qty | decimal(18,4) | الكمية |
| UnitCost | decimal(18,4) | تكلفة الوحدة |

**العلاقات**:
- `OutEntryId` → `StockLedger.EntryId`
- `InEntryId` → `StockLedger.EntryId`

#### StockBatches
رصيد سريع للتشغيلات (Materialized View)

| العمود | النوع | الوصف |
|--------|------|-------|
| Id | int (PK) | المعرف |
| WarehouseId | int (FK) | المخزن |
| ProdId | nvarchar(50) (FK) | الصنف |
| BatchNo | nvarchar(50) | رقم التشغيلة |
| Expiry | date | تاريخ الصلاحية |
| QtyOnHand | decimal(18,4) | الرصيد الحالي |
| QtyReserved | decimal(18,4) | الكمية المحجوزة |

**Unique Constraint**:
- `(WarehouseId, ProdId, BatchNo, Expiry)` - صف واحد فقط لكل مجموعة

#### Batches
ماستر التشغيلات

| العمود | النوع | الوصف |
|--------|------|-------|
| Id | int (PK) | المعرف |
| ProdId | nvarchar(50) (FK) | الصنف |
| BatchNo | nvarchar(50) | رقم التشغيلة |
| Expiry | date | تاريخ الصلاحية |
| PriceRetailBatch | decimal(18,2) | سعر الجمهور للتشغيلة |
| UnitCostDefault | decimal(18,4) | تكلفة الوحدة الافتراضية |

**العلاقات**:
- `ProdId` → `Products.ProdId`

---

### 4. الماستر داتا (Master Data)

#### Products
الأصناف

| العمود | النوع | الوصف |
|--------|------|-------|
| ProdId | nvarchar(50) (PK) | كود الصنف |
| ProdName | nvarchar(200) | اسم الصنف |
| Barcode | nvarchar(100) | الباركود |
| CategoryId | nvarchar(50) (FK) | الفئة |
| ProductGroupId | int (FK, nullable) | مجموعة الأصناف |
| PriceRetail | decimal(18,2) | سعر الجمهور |
| IsActive | bit | نشط؟ |

**العلاقات**:
- `CategoryId` → `Categories.CategoryId`
- `ProductGroupId` → `ProductGroups.ProductGroupId`

#### Customers
العملاء/الموردين

| العمود | النوع | الوصف |
|--------|------|-------|
| CustomerId | int (PK) | كود العميل |
| CustomerName | nvarchar(200) | اسم العميل |
| Phone1 | nvarchar(20) | الهاتف 1 |
| Address | nvarchar(250) | العنوان |
| GovernorateId | int (FK, nullable) | المحافظة |
| DistrictId | int (FK, nullable) | الحي/المركز |
| AreaId | int (FK, nullable) | المنطقة |
| PolicyId | int (FK, nullable) | السياسة |
| CreditLimit | decimal(18,2) | الحد الائتماني |
| CurrentBalance | decimal(18,2) | الرصيد الحالي |

**العلاقات**:
- `GovernorateId` → `Governorates.GovernorateId`
- `DistrictId` → `Districts.DistrictId`
- `AreaId` → `Areas.AreaId`
- `PolicyId` → `Policies.PolicyId`

#### Warehouses
المخازن

| العمود | النوع | الوصف |
|--------|------|-------|
| WarehouseId | int (PK) | كود المخزن |
| WarehouseName | nvarchar(200) | اسم المخزن |
| BranchId | int (FK) | الفرع |
| IsActive | bit | نشط؟ |

**العلاقات**:
- `BranchId` → `Branches.BranchId`

---

### 5. الحسابات (Accounting)

#### LedgerEntries
دفتر الأستاذ العام

| العمود | النوع | الوصف |
|--------|------|-------|
| Id | int (PK) | رقم القيد |
| EntryDate | date | تاريخ القيد |
| AccountId | int (FK) | الحساب |
| CustomerId | int (FK, nullable) | العميل/المورد |
| SourceType | nvarchar(50) | نوع المصدر |
| SourceId | int | رقم المستند |
| PostVersion | int | رقم المرحلة |
| LineNo | int | رقم السطر |
| Debit | decimal(18,2) | مدين |
| Credit | decimal(18,2) | دائن |
| Description | nvarchar(250) | البيان |

**العلاقات**:
- `AccountId` → `Accounts.AccountId`
- `CustomerId` → `Customers.CustomerId`

**الفهارس**:
- `(SourceType, SourceId)`
- `(AccountId, EntryDate)`
- `(CustomerId, EntryDate)`

#### Accounts
شجرة الحسابات

| العمود | النوع | الوصف |
|--------|------|-------|
| AccountId | int (PK) | كود الحساب |
| AccountCode | nvarchar(50) | كود الحساب (فريد) |
| AccountName | nvarchar(200) | اسم الحساب |
| AccountType | int | نوع الحساب (enum) |
| ParentAccountId | int (FK, nullable) | الحساب الأب |
| Level | int | المستوى في الشجرة |
| IsLeaf | bit | حساب نهائي؟ |
| IsActive | bit | نشط؟ |

**العلاقات**:
- `ParentAccountId` → `Accounts.AccountId` (Self-referencing)

---

### 6. الأمان (Security)

#### Users
المستخدمين

| العمود | النوع | الوصف |
|--------|------|-------|
| UserId | int (PK) | رقم المستخدم |
| UserName | nvarchar(50) | اسم الدخول (فريد) |
| DisplayName | nvarchar(150) | الاسم المعروض |
| PasswordHash | nvarchar(256) | كلمة المرور المشفرة |
| IsAdmin | bit | مدير؟ |
| IsActive | bit | نشط؟ |
| CreatedAt | datetime2 | تاريخ الإنشاء |

#### Roles
الأدوار

| العمود | النوع | الوصف |
|--------|------|-------|
| RoleId | int (PK) | رقم الدور |
| Name | nvarchar(50) | اسم الدور (فريد) |
| CreatedAt | datetime2 | تاريخ الإنشاء |

#### Permissions
الصلاحيات

| العمود | النوع | الوصف |
|--------|------|-------|
| PermissionId | int (PK) | رقم الصلاحية |
| Code | nvarchar(100) | كود الصلاحية (فريد) |
| NameAr | nvarchar(200) | الاسم بالعربي |
| Module | nvarchar(100) | الموديول |

#### UserRoles
ربط المستخدمين بالأدوار

| العمود | النوع | الوصف |
|--------|------|-------|
| Id | int (PK) | المعرف |
| UserId | int (FK) | المستخدم |
| RoleId | int (FK) | الدور |

**العلاقات**:
- `UserId` → `Users.UserId`
- `RoleId` → `Roles.RoleId`

#### RolePermissions
ربط الأدوار بالصلاحيات

| العمود | النوع | الوصف |
|--------|------|-------|
| Id | int (PK) | المعرف |
| RoleId | int (FK) | الدور |
| PermissionId | int (FK) | الصلاحية |

**العلاقات**:
- `RoleId` → `Roles.RoleId`
- `PermissionId` → `Permissions.PermissionId`

---

## 🔗 العلاقات الرئيسية

### One-to-Many

```
SalesInvoice (1) ──→ (N) SalesInvoiceLine
PurchaseInvoice (1) ──→ (N) PILine
Customer (1) ──→ (N) SalesInvoice
Warehouse (1) ──→ (N) StockLedger
Product (1) ──→ (N) StockLedger
Account (1) ──→ (N) LedgerEntry
```

### Many-to-Many

```
User (N) ←──→ (N) Role (عبر UserRoles)
Role (N) ←──→ (N) Permission (عبر RolePermissions)
```

### Self-Referencing

```
Account (Parent) ──→ (Children) Account
```

---

## 📈 الفهارس المهمة

### فهارس الأداء

```sql
-- StockLedger: للبحث السريع
CREATE INDEX IX_StockLedger_Warehouse_Product 
ON StockLedger(WarehouseId, ProdId);

-- StockLedger: للـ FIFO
CREATE INDEX IX_StockLedger_Fifo 
ON StockLedger(WarehouseId, ProdId, Expiry, TranDate, EntryId);

-- LedgerEntries: للبحث بالمستند
CREATE INDEX IX_LedgerEntries_Source 
ON LedgerEntries(SourceType, SourceId);

-- LedgerEntries: كشف حساب
CREATE INDEX IX_LedgerEntries_Account_Date 
ON LedgerEntries(AccountId, EntryDate);
```

### Unique Constraints

```sql
-- StockBatches: صف واحد لكل (مخزن + صنف + تشغيلة + صلاحية)
CREATE UNIQUE INDEX UX_StockBatches_Unique 
ON StockBatches(WarehouseId, ProdId, BatchNo, Expiry);

-- Users: اسم الدخول فريد
CREATE UNIQUE INDEX UX_Users_UserName 
ON Users(UserName);

-- Permissions: كود الصلاحية فريد
CREATE UNIQUE INDEX UX_Permissions_Code 
ON Permissions(Code);
```

---

## 🔄 Constraints

### Check Constraints

```sql
-- StockLedger: الكمية موجبة
ALTER TABLE StockLedger
ADD CONSTRAINT CK_Stock_Qty_Positive
CHECK (QtyIn >= 0 AND QtyOut >= 0);

-- SalesInvoiceLines: الكمية موجبة
ALTER TABLE SalesInvoiceLines
ADD CONSTRAINT CK_SIL_Qty_Positive
CHECK (Qty > 0);

-- StockTransfer: المخزن المصدر ≠ المخزن الوجهة
ALTER TABLE StockTransfers
ADD CONSTRAINT CK_StockTransfer_Warehouses
CHECK (FromWarehouseId <> ToWarehouseId);
```

---

## 📊 حجم البيانات المتوقع

| الجدول | حجم متوقع | ملاحظات |
|--------|-----------|---------|
| StockLedger | ملايين السطور | كل حركة مخزنية |
| LedgerEntries | مئات الآلاف | كل قيد محاسبي |
| SalesInvoices | عشرات الآلاف | فواتير البيع |
| PurchaseInvoices | عشرات الآلاف | فواتير الشراء |
| Products | آلاف | الأصناف |
| Customers | آلاف | العملاء/الموردين |

---

## 🔧 Maintenance

### تنظيف البيانات القديمة

```sql
-- حذف سجلات النشاط القديمة (أكثر من سنة)
DELETE FROM UserActivityLogs
WHERE ActionTime < DATEADD(YEAR, -1, GETDATE());
```

### إعادة بناء الفهارس

```sql
-- إعادة بناء فهرس
ALTER INDEX IX_StockLedger_Warehouse_Product 
ON StockLedger REBUILD;
```

---

**آخر تحديث**: مارس 2026
