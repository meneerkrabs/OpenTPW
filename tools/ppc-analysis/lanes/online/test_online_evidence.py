"""Tests for the online-service evidence lane (layout tables, chat satellites, library classes)."""
import os
from pathlib import Path
import unittest

from online_evidence import inspect, ONLINE_TABLES, SATELLITE_IDS


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'private corpus paths not supplied')
class OnlineEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))

    def table(self, name):
        return {window['id']: window for window in self.report['tables'][name]['windows']}

    def test_every_online_dialog_builds_from_a_layout_stream(self):
        # Each online screen function loads exactly one table from the data
        # section before calling the shared layout builder.
        for name, entry in self.report['tables'].items():
            windows = self.report['tables'][name]['windows']
            self.assertGreaterEqual(len(windows), 2, f'{name} has a root plus controls')
            self.assertIn(name, ONLINE_TABLES)

    def test_login_dialog_composition(self):
        windows = self.table('login_dialog')
        self.assertEqual(windows[72]['rectangle'], [500, 233, 1516, 830])
        # Name and password fields with their labels inside, and the Login
        # button area at the top (0x49-0x4d are the Mac control ids of
        # docs/reverse/UI-MAP.md's login dialog).
        self.assertEqual(windows[73]['type'], 5)
        self.assertEqual(windows[74]['parent'], 73)
        self.assertEqual(windows[74]['rectangle'], [656, 487, 946, 532])
        self.assertEqual(windows[75]['type'], 5)
        self.assertEqual(windows[76]['parent'], 75)
        self.assertEqual(windows[77]['rectangle'], [606, 304, 1333, 384])
        self.assertIn(-1, windows)
        self.assertIn(-2, windows)

    def test_news_panel_has_two_column_tabs_and_a_title(self):
        windows = self.table('news_panel')
        self.assertEqual(windows[23422]['rectangle'], [186, 30, 2018, 1007])
        tabs = self.table('news_panel')[23425]
        self.assertEqual(tabs['rectangle'], [1024, 224, 1695, 294])
        self.assertEqual(self.table('news_panel')[23426]['rectangle'], [1024, 224, 1348, 294])
        self.assertEqual(self.table('news_panel')[23427]['rectangle'], [1372, 224, 1695, 294])
        self.assertEqual(windows[23428]['rectangle'], [822, 98, 1295, 177])

    def test_find_parks_six_column_list_and_search_row(self):
        windows = self.table('find_parks')
        self.assertEqual(windows[57432]['rectangle'], [108, 30, 1940, 1007])
        headers = sorted((window for window in self.report['tables']['find_parks']['windows']
                          if window['origin_command'] == 12), key=lambda window: window['rectangle'][0])
        self.assertEqual([header['id'] for header in headers], [16, 17, 18, 19, 20, 21])
        self.assertEqual([header['rectangle'][0] for header in headers], [225, 565, 881, 1175, 1377, 1509])
        self.assertEqual(windows[57433]['rectangle'], [1179, 810, 1332, 964])
        self.assertEqual(windows[57434]['type'], 5)
        self.assertEqual(windows[57434]['rectangle'], [380, 839, 1122, 938])

    def test_park_details_label_rows_and_action_buttons(self):
        windows = self.table('park_details')
        self.assertEqual(windows[940333]['rectangle'], [248, 30, 1800, 1007])
        rows = sorted((window for window in self.report['tables']['park_details']['windows']
                       if window['parent'] == 940338), key=lambda window: window['rectangle'][1])
        self.assertEqual([window['id'] for window in rows],
                         [940343, 940340, 940345, 940342, 940346, 940344, 940339, 940341])
        self.assertEqual(windows[940336]['rectangle'], [922, 207, 1602, 655])
        self.assertEqual(windows[940335]['rectangle'], [1322, 800, 1476, 953])
        self.assertEqual(windows[940334]['rectangle'], [1481, 800, 1635, 953])

    def test_publish_park_two_fields_and_check_button(self):
        windows = self.table('publish_park')
        self.assertEqual(windows[4580]['rectangle'], [682, 279, 1366, 372])
        self.assertEqual(windows[4579]['rectangle'], [684, 444, 1364, 892])
        self.assertEqual(windows[4579]['attributes'], 129)
        self.assertEqual(windows[4581]['rectangle'], [788, 75, 1260, 155])
        self.assertEqual(windows[-1]['rectangle'], [1660, 731, 1743, 815])

    def test_unsent_postcards_bulk_buttons_and_four_columns(self):
        windows = self.table('unsent_postcards')
        self.assertEqual(windows[3813]['rectangle'], [265, 792, 419, 945])
        self.assertEqual(windows[3814]['rectangle'], [438, 792, 591, 945])
        self.assertEqual(windows[3815]['rectangle'], [610, 792, 764, 945])
        headers = sorted((window for window in self.report['tables']['unsent_postcards']['windows']
                          if window['origin_command'] == 12), key=lambda window: window['rectangle'][0])
        self.assertEqual([header['id'] for header in headers], [16, 17, 18, 19])
        self.assertEqual(windows[3816]['rectangle'], [793, 151, 1823, 944])

    def test_world_lobby_places_five_satellites(self):
        windows = self.table('world_lobby')
        for index, identifier in enumerate(SATELLITE_IDS):
            self.assertEqual(windows[identifier]['parent'], 90119)
        self.assertEqual(windows[90120]['rectangle'], [131, 139, 438, 446])
        self.assertEqual(windows[90124]['rectangle'], [1335, 1199, 1643, 1506])

    def test_chatellite_connector_counts_chat_park_players(self):
        chatellites = self.report['chatellites']
        self.assertIn('ONLINE CHATELLITES: connection failed!\n', chatellites['failure_string'])
        self.assertEqual(chatellites['format'], ['%d'])
        self.assertEqual(chatellites['satellite_controls'], SATELLITE_IDS)
        self.assertTrue(chatellites['at_most_five'])
        self.assertTrue(any(name.startswith('Connect__4Chat') for name in chatellites['connect_import']))
        self.assertTrue(any(name.startswith('RoomInfo__4Chat') for name in chatellites['room_info_import']))

    def test_online_shared_library_export_surface(self):
        exports = set(self.report['online_classes'])
        for method in ('GetCityList', 'GetParkInCityListTop10', 'UnpublishPark', 'MarkParkVisited',
                       'Login', 'GetGameNews', 'GetServerNews', 'GetGameServerDetails', 'SendVote',
                       'RoomInfo', 'EnumPlayers', 'EnumRooms', 'MovePlayer', 'SetHearingRange',
                       'CreateRoom', 'CreatePlayer', 'StartGame', 'GameConfig', 'UploadParkRequest'):
            self.assertIn(method, exports)


if __name__ == '__main__':
    unittest.main()
