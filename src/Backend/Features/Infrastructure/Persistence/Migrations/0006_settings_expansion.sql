INSERT INTO app_settings (key, value) VALUES
    ('ollama.host',              'http://localhost:11434'),
    ('ollama.api_key',           ''),
    ('ollama.num_ctx',           '16384'),
    ('ollama.temperature',       '0.3'),
    ('calendar.google.client_id',     ''),
    ('calendar.google.client_secret', ''),
    ('calendar.google.calendar_id',   'primary'),
    ('calendar.working_hours_start',  '09:00'),
    ('calendar.working_hours_end',    '18:00'),
    ('calendar.default_duration_min', '60'),
    ('calendar.search_horizon_days',  '14')
ON CONFLICT(key) DO NOTHING;
