namespace Landoria.WorldCrawler.Runtime
{
    // Applies the native local tod override on arrival without modifying synchronized world time.
    internal sealed class LocalDaylight
    {
        private EnvMan _environment;
        private Player _player;
        private bool _previousEnabled;
        private float _previousTime;

        // Reapplies once per environment/player arrival rather than issuing console cheat commands.
        internal void Update()
        {
            var player = Player.m_localPlayer;
            var environment = EnvMan.instance;
            if (player == null || environment == null)
            {
                Reset();
                return;
            }
            if (_environment == environment && _player == player)
            {
                return;
            }
            Reset();
            _environment = environment;
            _player = player;
            _previousEnabled = environment.m_debugTimeOfDay;
            _previousTime = environment.m_debugTime;
            environment.m_debugTimeOfDay = true;
            environment.m_debugTime = 0.4f;
        }

        // Restores only the override still owned by this plugin when leaving or unloading it.
        internal void Reset()
        {
            if (_environment != null && _environment.m_debugTimeOfDay && _environment.m_debugTime == 0.4f)
            {
                _environment.m_debugTimeOfDay = _previousEnabled;
                _environment.m_debugTime = _previousTime;
            }
            _environment = null;
            _player = null;
        }
    }
}
