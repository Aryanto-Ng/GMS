-- Uji constraint migrasi GMS terhadap PostgreSQL nyata.
-- Setiap percobaan dijalankan dalam sub-transaksi yang SELALU di-rollback; hasil dicatat di tabel results.
\set ON_ERROR_STOP off
\pset pager off
\pset format aligned

-- ===== Fixture (di-commit) =====
INSERT INTO app_roles(id, name, is_system, created_at) VALUES ('00000000-0000-0000-0000-0000000000a1','TestRole',false,now());
INSERT INTO users(id,user_name,normalized_user_name,full_name,role_id,is_active,must_change_password,created_at,email_confirmed,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES
 ('00000000-0000-0000-0000-0000000000b1','u1','U1','User 1','00000000-0000-0000-0000-0000000000a1',true,false,now(),false,false,false,true,0),
 ('00000000-0000-0000-0000-0000000000b2','u2','U2','User 2','00000000-0000-0000-0000-0000000000a1',true,false,now(),false,false,false,true,0);

INSERT INTO uoms(id,code,name,is_active,created_at) VALUES ('00000000-0000-0000-0000-0000000000c1','PCS','Pieces',true,now());
INSERT INTO sizes(id,code,name,sort_order,is_active,created_at) VALUES
 ('00000000-0000-0000-0000-0000000000d1','S','Small',1,true,now()),
 ('00000000-0000-0000-0000-0000000000d2','M','Medium',2,true,now()),
 ('00000000-0000-0000-0000-0000000000d3','L','Large',3,true,now());
INSERT INTO colors(id,code,name,hex,is_active,created_at) VALUES ('00000000-0000-0000-0000-0000000000e1','NAVY','Navy','#001F3F',true,now());
INSERT INTO customers(id,code,name,is_active,created_at) VALUES
 ('00000000-0000-0000-0000-0000000000f1','CUS-0001','PT Maju',true,now()),
 ('00000000-0000-0000-0000-0000000000f2','CUS-0002','PT Jaya',true,now());
INSERT INTO fgs(id,code,name,is_active,created_at) VALUES ('00000000-0000-0000-0000-000000000101','KMJ-001','Kemeja',true,now());
INSERT INTO processes(id,code,name,type,is_active,created_at) VALUES
 ('00000000-0000-0000-0000-000000000111','CUT','Cutting','Internal',true,now()),
 ('00000000-0000-0000-0000-000000000112','EMB','Bordir','Vendor',true,now()),
 ('00000000-0000-0000-0000-000000000113','SEW','Sewing','Internal',true,now());
INSERT INTO fg_sizes(fg_id,size_id) VALUES ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000d1'),('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000d2');
INSERT INTO fg_routing_steps(id,fg_id,process_id,seq) VALUES
 ('00000000-0000-0000-0000-000000000121','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000111',1),
 ('00000000-0000-0000-0000-000000000122','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000112',2),
 ('00000000-0000-0000-0000-000000000123','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000113',3);
INSERT INTO fg_tariffs(fg_id,process_id,size_id,rate) VALUES
 ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000111','00000000-0000-0000-0000-0000000000d1',350.12345),
 ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000113','00000000-0000-0000-0000-0000000000d1',2500);

INSERT INTO purchase_orders(id,code,customer_id,customer_po_number,po_date,status,created_at) VALUES
 ('00000000-0000-0000-0000-000000000201','PO-202611-0001','00000000-0000-0000-0000-0000000000f1','PO/25/1','2026-11-01','Confirmed',now());
INSERT INTO po_lines(id,po_id,fg_id,color_id,size_id,delivery_date,qty,delivered_qty,allocated_qty,in_production_qty,created_at) VALUES
 ('00000000-0000-0000-0000-000000000211','00000000-0000-0000-0000-000000000201','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000d1','2026-11-20',100,0,0,0,now());
INSERT INTO workers(id,code,name,is_active,created_at) VALUES ('00000000-0000-0000-0000-000000000301','PKJ-0001','Budi',true,now());
INSERT INTO work_orders(id,code,po_id,fg_id,fg_code,fg_name,status,created_at) VALUES
 ('00000000-0000-0000-0000-000000000401','WO-202611-0001','00000000-0000-0000-0000-000000000201','00000000-0000-0000-0000-000000000101','KMJ-001','Kemeja','Released',now());
INSERT INTO wo_lines(id,wo_id,po_line_id,qty,handed_over_qty,color_code,color_name,size_code,size_name,created_at) VALUES
 ('00000000-0000-0000-0000-000000000411','00000000-0000-0000-0000-000000000401','00000000-0000-0000-0000-000000000211',100,0,'NAVY','Navy','S','Small',now());
INSERT INTO wo_routing_snapshot(wo_id,seq,process_id,process_code,process_name,process_type) VALUES
 ('00000000-0000-0000-0000-000000000401',1,'00000000-0000-0000-0000-000000000111','CUT','Cutting','Internal');
INSERT INTO output_entries(id,wo_id,seq,worker_id,work_date,kind,status,created_at) VALUES
 ('00000000-0000-0000-0000-000000000421','00000000-0000-0000-0000-000000000401',1,'00000000-0000-0000-0000-000000000301','2026-11-05','Output','Active',now());
INSERT INTO payroll_periods(id,code,start_date,end_date,status,created_at) VALUES
 ('00000000-0000-0000-0000-000000000501','GJ-20261101-20261107','2026-11-01','2026-11-07','Closed',now()),
 ('00000000-0000-0000-0000-000000000502','GJ-20261108-20261114','2026-11-08','2026-11-14','Open',now());
INSERT INTO user_sessions(id,user_id,token_hash,created_at,last_activity_at) VALUES
 ('00000000-0000-0000-0000-000000000601','00000000-0000-0000-0000-0000000000b1','\x01',now(),now());
INSERT INTO audit_logs(occurred_at,event_type) VALUES (now(),'Fixture');
INSERT INTO stock_balances(fg_id,color_id,size_id,on_hand,allocated) VALUES
 ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000d1',50,10);

-- ===== Harness =====
CREATE TEMP TABLE results(seq serial, test text, want text, got text, ok boolean);

CREATE FUNCTION pg_temp.attempt(label text, stmt text, want text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE st text;
BEGIN
  BEGIN
    EXECUTE stmt;
    RAISE EXCEPTION 'attempt-succeeded' USING ERRCODE = 'ZZ001';
  EXCEPTION WHEN OTHERS THEN
    st := SQLSTATE;
  END;
  INSERT INTO results(test,want,got,ok)
  VALUES (label, want, CASE WHEN st = 'ZZ001' THEN 'OK' ELSE st END, (CASE WHEN st = 'ZZ001' THEN 'OK' ELSE st END) = want);
END $$;

-- ===== Uji =====
-- citext unik tidak peka huruf besar-kecil
SELECT pg_temp.attempt('uoms: kode "pcs" ditolak karena "PCS" sudah ada', $$INSERT INTO uoms(id,code,name,is_active,created_at) VALUES (gen_random_uuid(),'pcs','x',true,now())$$, '23505');
SELECT pg_temp.attempt('uoms: kode baru diterima', $$INSERT INTO uoms(id,code,name,is_active,created_at) VALUES (gen_random_uuid(),'KG','Kilogram',true,now())$$, 'OK');
SELECT pg_temp.attempt('customers: nama sama beda huruf ditolak', $$INSERT INTO customers(id,code,name,is_active,created_at) VALUES (gen_random_uuid(),'CUS-0009','pt maju',true,now())$$, '23505');

-- CHECK pada master
SELECT pg_temp.attempt('sizes: sort_order negatif ditolak', $$INSERT INTO sizes(id,code,name,sort_order,is_active,created_at) VALUES (gen_random_uuid(),'XL','XL',-1,true,now())$$, '23514');
SELECT pg_temp.attempt('colors: hex tidak valid ditolak', $$INSERT INTO colors(id,code,name,hex,is_active,created_at) VALUES (gen_random_uuid(),'RED','Red','red',true,now())$$, '23514');
SELECT pg_temp.attempt('colors: hex valid diterima', $$INSERT INTO colors(id,code,name,hex,is_active,created_at) VALUES (gen_random_uuid(),'RED','Red','#FF0000',true,now())$$, 'OK');
SELECT pg_temp.attempt('processes: tipe tidak valid ditolak', $$INSERT INTO processes(id,code,name,type,is_active,created_at) VALUES (gen_random_uuid(),'X','X','Hybrid',true,now())$$, '23514');

-- Sesi tunggal
SELECT pg_temp.attempt('user_sessions: sesi aktif kedua u1 ditolak', $$INSERT INTO user_sessions(id,user_id,token_hash,created_at,last_activity_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','\x02',now(),now())$$, '23505');
SELECT pg_temp.attempt('user_sessions: sesi yang sudah dicabut boleh banyak', $$INSERT INTO user_sessions(id,user_id,token_hash,created_at,last_activity_at,revoked_at,revoked_reason) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','\x03',now(),now(),now(),'Replaced')$$, 'OK');
SELECT pg_temp.attempt('user_sessions: sesi aktif untuk user lain diterima', $$INSERT INTO user_sessions(id,user_id,token_hash,created_at,last_activity_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b2','\x04',now(),now())$$, 'OK');
SELECT pg_temp.attempt('user_sessions: token_hash ganda ditolak', $$INSERT INTO user_sessions(id,user_id,token_hash,created_at,last_activity_at,revoked_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b2','\x01',now(),now(),now())$$, '23505');

-- Periode gaji
SELECT pg_temp.attempt('payroll: periode tumpang tindih ditolak (exclusion)', $$INSERT INTO payroll_periods(id,code,start_date,end_date,status,created_at) VALUES (gen_random_uuid(),'GJ-X1','2026-11-05','2026-11-10','Closed',now())$$, '23P01');
SELECT pg_temp.attempt('payroll: periode berimpit di batas hari ditolak', $$INSERT INTO payroll_periods(id,code,start_date,end_date,status,created_at) VALUES (gen_random_uuid(),'GJ-X2','2026-11-07','2026-11-09','Closed',now())$$, '23P01');
SELECT pg_temp.attempt('payroll: periode berurutan tanpa celah diterima (Closed)', $$INSERT INTO payroll_periods(id,code,start_date,end_date,status,created_at) VALUES (gen_random_uuid(),'GJ-X3','2026-11-15','2026-11-21','Closed',now())$$, 'OK');
SELECT pg_temp.attempt('payroll: periode Open kedua ditolak', $$INSERT INTO payroll_periods(id,code,start_date,end_date,status,created_at) VALUES (gen_random_uuid(),'GJ-X4','2026-11-15','2026-11-21','Open',now())$$, '23505');
SELECT pg_temp.attempt('payroll: end_date < start_date ditolak', $$INSERT INTO payroll_periods(id,code,start_date,end_date,status,created_at) VALUES (gen_random_uuid(),'GJ-X5','2026-11-21','2026-11-15','Closed',now())$$, '23514');
SELECT pg_temp.attempt('payroll: status tidak valid ditolak', $$INSERT INTO payroll_periods(id,code,start_date,end_date,status,created_at) VALUES (gen_random_uuid(),'GJ-X6','2026-12-01','2026-12-07','Draft',now())$$, '23514');

-- Routing dan tarif
SELECT pg_temp.attempt('routing: proses ganda dalam satu FG ditolak', $$INSERT INTO fg_routing_steps(id,fg_id,process_id,seq) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000111',9)$$, '23505');
SELECT pg_temp.attempt('routing: seq 0 ditolak', $$INSERT INTO fg_routing_steps(id,fg_id,process_id,seq) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000112',0)$$, '23514');
SELECT pg_temp.attempt('tarif: untuk proses yang tidak ada di routing ditolak (FK)', $$INSERT INTO processes(id,code,name,type,is_active,created_at) VALUES ('00000000-0000-0000-0000-000000000114','QC','QC','Internal',true,now()); INSERT INTO fg_tariffs(fg_id,process_id,size_id,rate) VALUES ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000114','00000000-0000-0000-0000-0000000000d1',100)$$, '23503');
SELECT pg_temp.attempt('tarif: untuk size yang bukan Size Berlaku FG ditolak (FK)', $$INSERT INTO fg_tariffs(fg_id,process_id,size_id,rate) VALUES ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000111','00000000-0000-0000-0000-0000000000d3',100)$$, '23503');
SELECT pg_temp.attempt('tarif: rate 0 ditolak', $$INSERT INTO fg_tariffs(fg_id,process_id,size_id,rate) VALUES ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000111','00000000-0000-0000-0000-0000000000d2',0)$$, '23514');
SELECT pg_temp.attempt('tarif: rate 5 digit desimal diterima', $$INSERT INTO fg_tariffs(fg_id,process_id,size_id,rate) VALUES ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000111','00000000-0000-0000-0000-0000000000d2',1250.12345)$$, 'OK');

-- PO dan baris PO
SELECT pg_temp.attempt('po_lines: qty 0 ditolak', $$INSERT INTO po_lines(id,po_id,fg_id,color_id,size_id,delivery_date,qty,delivered_qty,allocated_qty,in_production_qty,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000201','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000d2','2026-11-20',0,0,0,0,now())$$, '23514');
SELECT pg_temp.attempt('po_lines: qty 1.000.001 ditolak', $$INSERT INTO po_lines(id,po_id,fg_id,color_id,size_id,delivery_date,qty,delivered_qty,allocated_qty,in_production_qty,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000201','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000d2','2026-11-20',1000001,0,0,0,now())$$, '23514');
SELECT pg_temp.attempt('po_lines: Sisa Rencana minus (alokasi+produksi > qty) ditolak', $$UPDATE po_lines SET allocated_qty = 60, in_production_qty = 41 WHERE id='00000000-0000-0000-0000-000000000211'$$, '23514');
SELECT pg_temp.attempt('po_lines: Sisa Rencana tepat nol diterima', $$UPDATE po_lines SET delivered_qty = 40, allocated_qty = 30, in_production_qty = 30 WHERE id='00000000-0000-0000-0000-000000000211'$$, 'OK');
SELECT pg_temp.attempt('po_lines: counter negatif ditolak', $$UPDATE po_lines SET delivered_qty = -1 WHERE id='00000000-0000-0000-0000-000000000211'$$, '23514');
SELECT pg_temp.attempt('po_lines: kombinasi FG/Color/Size/tanggal ganda ditolak', $$INSERT INTO po_lines(id,po_id,fg_id,color_id,size_id,delivery_date,qty,delivered_qty,allocated_qty,in_production_qty,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000201','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000d1','2026-11-20',10,0,0,0,now())$$, '23505');
SELECT pg_temp.attempt('po: nomor PO customer sama (beda huruf) di customer sama ditolak', $$INSERT INTO purchase_orders(id,code,customer_id,customer_po_number,po_date,status,created_at) VALUES (gen_random_uuid(),'PO-X1','00000000-0000-0000-0000-0000000000f1','po/25/1','2026-11-02','Draft',now())$$, '23505');
SELECT pg_temp.attempt('po: nomor sama di customer lain diterima', $$INSERT INTO purchase_orders(id,code,customer_id,customer_po_number,po_date,status,created_at) VALUES (gen_random_uuid(),'PO-X2','00000000-0000-0000-0000-0000000000f2','PO/25/1','2026-11-02','Draft',now())$$, 'OK');
SELECT pg_temp.attempt('po: nomor kosong boleh ganda', $$INSERT INTO purchase_orders(id,code,customer_id,customer_po_number,po_date,status,created_at) VALUES (gen_random_uuid(),'PO-X3','00000000-0000-0000-0000-0000000000f1',NULL,'2026-11-02','Draft',now()),(gen_random_uuid(),'PO-X4','00000000-0000-0000-0000-0000000000f1',NULL,'2026-11-02','Draft',now())$$, 'OK');
SELECT pg_temp.attempt('po: nomor dari PO Cancelled boleh dipakai ulang', $$UPDATE purchase_orders SET status='Cancelled' WHERE id='00000000-0000-0000-0000-000000000201'; INSERT INTO purchase_orders(id,code,customer_id,customer_po_number,po_date,status,created_at) VALUES (gen_random_uuid(),'PO-X5','00000000-0000-0000-0000-0000000000f1','PO/25/1','2026-11-02','Draft',now())$$, 'OK');
SELECT pg_temp.attempt('po: status tidak valid ditolak', $$UPDATE purchase_orders SET status='Done' WHERE id='00000000-0000-0000-0000-000000000201'$$, '23514');
SELECT pg_temp.attempt('customers: tidak bisa dihapus bila dipakai PO (RESTRICT)', $$DELETE FROM customers WHERE id='00000000-0000-0000-0000-0000000000f1'$$, '23503');

-- Stok
SELECT pg_temp.attempt('stock_balances: on_hand negatif ditolak', $$UPDATE stock_balances SET on_hand = -1, allocated = 0$$, '23514');
SELECT pg_temp.attempt('stock_balances: teralokasi melebihi fisik ditolak', $$UPDATE stock_balances SET allocated = 51$$, '23514');
SELECT pg_temp.attempt('stock_balances: teralokasi = fisik diterima', $$UPDATE stock_balances SET allocated = 50$$, 'OK');
SELECT pg_temp.attempt('stock_ledger: qty 0 ditolak', $$INSERT INTO stock_ledger(fg_id,color_id,size_id,kind,qty,entry_date,created_at) VALUES ('00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000d1','Adjustment',0,'2026-11-08',now())$$, '23514');

-- Work order dan output
SELECT pg_temp.attempt('work_orders: target < mulai ditolak', $$UPDATE work_orders SET start_date='2026-11-10', target_date='2026-11-09'$$, '23514');
SELECT pg_temp.attempt('wo_lines: qty 0 ditolak', $$UPDATE wo_lines SET qty = 0$$, '23514');
SELECT pg_temp.attempt('wo_tariff_snapshot: rate 0 ditolak', $$INSERT INTO wo_tariff_snapshot(wo_id,seq,size_id,size_code,rate) VALUES ('00000000-0000-0000-0000-000000000401',1,'00000000-0000-0000-0000-0000000000d1','S',0)$$, '23514');
SELECT pg_temp.attempt('wo_tariff_snapshot: untuk langkah yang tidak ada di snapshot ditolak (FK)', $$INSERT INTO wo_tariff_snapshot(wo_id,seq,size_id,size_code,rate) VALUES ('00000000-0000-0000-0000-000000000401',7,'00000000-0000-0000-0000-0000000000d1','S',100)$$, '23503');
SELECT pg_temp.attempt('output_entries: langkah di luar routing snapshot ditolak (FK)', $$INSERT INTO output_entries(id,wo_id,seq,worker_id,work_date,kind,status,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000401',5,'00000000-0000-0000-0000-000000000301','2026-11-05','Output','Active',now())$$, '23503');
SELECT pg_temp.attempt('output_entries: Adjustment tanpa original_date/reason ditolak', $$INSERT INTO output_entries(id,wo_id,seq,worker_id,work_date,kind,status,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000401',1,'00000000-0000-0000-0000-000000000301','2026-11-09','Adjustment','Active',now())$$, '23514');
SELECT pg_temp.attempt('output_entries: Adjustment lengkap diterima', $$INSERT INTO output_entries(id,wo_id,seq,worker_id,work_date,kind,status,original_date,reason,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000000401',1,'00000000-0000-0000-0000-000000000301','2026-11-09','Adjustment','Active','2026-11-03','Salah input',now())$$, 'OK');
SELECT pg_temp.attempt('output_entries: Voided tanpa alasan ditolak', $$UPDATE output_entries SET status='Voided' WHERE id='00000000-0000-0000-0000-000000000421'$$, '23514');
SELECT pg_temp.attempt('output_entries: Voided dengan alasan diterima', $$UPDATE output_entries SET status='Voided', void_reason='Salah pekerja' WHERE id='00000000-0000-0000-0000-000000000421'$$, 'OK');
SELECT pg_temp.attempt('output_entry_lines: qty 0 ditolak', $$INSERT INTO output_entry_lines(entry_id,wo_line_id,qty) VALUES ('00000000-0000-0000-0000-000000000421','00000000-0000-0000-0000-000000000411',0)$$, '23514');
SELECT pg_temp.attempt('output_entry_lines: qty negatif diterima di DB (aturan Adjustment ditegakkan aplikasi)', $$INSERT INTO output_entry_lines(entry_id,wo_line_id,qty) VALUES ('00000000-0000-0000-0000-000000000421','00000000-0000-0000-0000-000000000411',-5)$$, 'OK');
SELECT pg_temp.attempt('wo_step_balances: qty negatif ditolak', $$INSERT INTO wo_step_balances(wo_line_id,seq,qty,sent_qty) VALUES ('00000000-0000-0000-0000-000000000411',1,-1,0)$$, '23514');
SELECT pg_temp.attempt('vendor_movement_lines/fg_handover_lines: auto_allocated > qty ditolak', $$INSERT INTO fg_handovers(id,wo_id,handover_date,status,created_at) VALUES ('00000000-0000-0000-0000-000000000701','00000000-0000-0000-0000-000000000401','2026-11-08','Active',now()); INSERT INTO fg_handover_lines(handover_id,wo_line_id,qty,auto_allocated_qty) VALUES ('00000000-0000-0000-0000-000000000701','00000000-0000-0000-0000-000000000411',10,11)$$, '23514');

-- Pengiriman
SELECT pg_temp.attempt('deliveries: void tanpa alasan ditolak', $$INSERT INTO deliveries(id,code,po_id,delivery_date,status,print_count,created_at) VALUES (gen_random_uuid(),'SJ-X1','00000000-0000-0000-0000-000000000201','2026-11-08','Voided',0,now())$$, '23514');
SELECT pg_temp.attempt('deliveries: print_count negatif ditolak', $$INSERT INTO deliveries(id,code,po_id,delivery_date,status,print_count,created_at) VALUES (gen_random_uuid(),'SJ-X2','00000000-0000-0000-0000-000000000201','2026-11-08','Active',-1,now())$$, '23514');
SELECT pg_temp.attempt('delivery_lines: qty 0 ditolak', $$INSERT INTO deliveries(id,code,po_id,delivery_date,status,print_count,created_at) VALUES ('00000000-0000-0000-0000-000000000801','SJ-X3','00000000-0000-0000-0000-000000000201','2026-11-08','Active',0,now()); INSERT INTO delivery_lines(delivery_id,po_line_id,qty) VALUES ('00000000-0000-0000-0000-000000000801','00000000-0000-0000-0000-000000000211',0)$$, '23514');

-- Audit immutability
SELECT pg_temp.attempt('audit_logs: UPDATE ditolak (trigger)', $$UPDATE audit_logs SET event_type='Diubah'$$, '23000');
SELECT pg_temp.attempt('audit_logs: DELETE ditolak (trigger)', $$DELETE FROM audit_logs$$, '23000');
SELECT pg_temp.attempt('audit_logs: TRUNCATE ditolak (trigger)', $$TRUNCATE audit_logs$$, '23000');
SELECT pg_temp.attempt('audit_logs: INSERT diterima', $$INSERT INTO audit_logs(occurred_at,event_type,old_value) VALUES (now(),'Uji','{"a":1}'::jsonb)$$, 'OK');

-- CASCADE
CREATE FUNCTION pg_temp.cascade_check(label text, setup_sql text, count_sql text, expected bigint) RETURNS void LANGUAGE plpgsql AS $$
DECLARE c bigint;
BEGIN
  BEGIN
    EXECUTE setup_sql;
    EXECUTE count_sql INTO c;
    RAISE EXCEPTION 'rollback' USING ERRCODE='ZZ002';
  EXCEPTION WHEN OTHERS THEN
    IF SQLSTATE <> 'ZZ002' THEN c := -1; END IF;
  END;
  INSERT INTO results(test,want,got,ok) VALUES (label, expected::text, c::text, c = expected);
END $$;
SELECT pg_temp.cascade_check('cascade: hapus langkah routing menghapus tarif langkah itu', $$DELETE FROM fg_routing_steps WHERE id='00000000-0000-0000-0000-000000000121'$$, $$SELECT count(*) FROM fg_tariffs WHERE process_id='00000000-0000-0000-0000-000000000111'$$, 0);
SELECT pg_temp.cascade_check('cascade: hapus langkah routing TIDAK menghapus tarif langkah lain', $$DELETE FROM fg_routing_steps WHERE id='00000000-0000-0000-0000-000000000121'$$, $$SELECT count(*) FROM fg_tariffs WHERE process_id='00000000-0000-0000-0000-000000000113'$$, 1);
SELECT pg_temp.cascade_check('cascade: hapus Size Berlaku menghapus tarif size itu', $$DELETE FROM fg_sizes WHERE size_id='00000000-0000-0000-0000-0000000000d1'$$, $$SELECT count(*) FROM fg_tariffs WHERE size_id='00000000-0000-0000-0000-0000000000d1'$$, 0);
SELECT pg_temp.cascade_check('cascade: hapus PO menghapus baris PO (jika tidak dipakai WO)', $$DELETE FROM work_orders; DELETE FROM purchase_orders WHERE id='00000000-0000-0000-0000-000000000201'$$, $$SELECT count(*) FROM po_lines WHERE po_id='00000000-0000-0000-0000-000000000201'$$, 0);

-- xmin berubah saat baris diubah (token konkurensi)
CREATE FUNCTION pg_temp.xmin_check() RETURNS void LANGUAGE plpgsql AS $$
DECLARE a xid; b xid;
BEGIN
  BEGIN
    SELECT xmin INTO a FROM po_lines WHERE id='00000000-0000-0000-0000-000000000211';
    UPDATE po_lines SET notes='x' WHERE id='00000000-0000-0000-0000-000000000211';
    SELECT xmin INTO b FROM po_lines WHERE id='00000000-0000-0000-0000-000000000211';
    RAISE EXCEPTION 'rollback' USING ERRCODE='ZZ002';
  EXCEPTION WHEN OTHERS THEN NULL;
  END;
  INSERT INTO results(test,want,got,ok) VALUES ('xmin: berubah setelah UPDATE (dasar token konkurensi)', 'berubah', CASE WHEN a::text <> b::text THEN 'berubah' ELSE 'tetap' END, a::text <> b::text);
END $$;
SELECT pg_temp.xmin_check();

-- ===== Laporan =====
\echo
\echo '===== HASIL UJI CONSTRAINT ====='
SELECT seq, CASE WHEN ok THEN 'LULUS' ELSE 'GAGAL' END AS status, test, want AS diharapkan, got AS didapat FROM results ORDER BY seq;
\echo
SELECT count(*) FILTER (WHERE ok) AS lulus, count(*) FILTER (WHERE NOT ok) AS gagal, count(*) AS total FROM results;
