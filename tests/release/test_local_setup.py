import importlib.util,json,tempfile,unittest,subprocess,sys
from pathlib import Path
spec=importlib.util.spec_from_file_location('setup',Path(__file__).parents[2]/'tools/configure_local.py')
setup=importlib.util.module_from_spec(spec);spec.loader.exec_module(setup)

class LocalSetup(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
  self.settings=self.root/'settings.json';self.registry=self.root/'obsidian.json'
 def tearDown(self):self.temp.cleanup()
 def vault(self,name='Notes',parent=None):
  p=(parent or self.root)/name;(p/'.obsidian').mkdir(parents=True);return p
 def test_no_vault_disables_only_optional_integration(self):
  old,new,notices=setup.plan(self.settings,self.registry)
  self.assertFalse(new['helperEnabled']['obsidian-links']);self.assertTrue(new['usageDialsEnabled'])
  self.assertEqual(new['helperConfig']['jarvis-file-links']['mappings'],[])
  self.assertTrue(notices);self.assertFalse(self.settings.exists())
 def test_cli_is_noninteractive_and_returns_json(self):
  command=[sys.executable,str(Path(setup.__file__)),'--settings',str(self.settings),'--obsidian-registry',str(self.registry)]
  report=json.loads(subprocess.check_output(command,text=True,stdin=subprocess.DEVNULL,timeout=10))
  self.assertFalse(report['applied']);self.assertFalse(self.settings.exists())
  report=json.loads(subprocess.check_output(command+['--apply'],text=True,stdin=subprocess.DEVNULL,timeout=10))
  self.assertTrue(report['applied']);self.assertTrue(self.settings.exists())
 def test_detects_local_vault_without_plugin_and_preserves_other_preferences(self):
  p=self.vault();self.registry.write_text(json.dumps({'vaults':{'a':{'path':str(p)}}}))
  self.settings.write_text(json.dumps({'forceHighPerformanceGpu':True,'customSetting':'keep','helperEnabled':{'usage-dials':False}}))
  old,new,_=setup.plan(self.settings,self.registry)
  self.assertEqual(new['helperConfig']['obsidian-links'],{'mappings':[{'sourcePrefix':p.as_posix()+'/','vault':'Notes'}],'uriAction':'open'})
  self.assertTrue(new['forceHighPerformanceGpu']);self.assertFalse(new['helperEnabled']['usage-dials'])
  backup=setup.save(self.settings,old,new);self.assertEqual(json.loads(Path(backup).read_text()),old)
  self.assertEqual(json.loads(self.settings.read_text())['customSetting'],'keep')
 def test_existing_mappings_are_never_overwritten_by_discovery(self):
  value={'helperConfig':{'obsidian-links':{'mappings':[{'sourcePrefix':'Z:/Notes/','vault':'My notes'}],'uriAction':'open'}}}
  self.settings.write_text(json.dumps(value));_,new,_=setup.plan(self.settings,self.registry)
  self.assertEqual(new['helperConfig'],{**value['helperConfig'],'jarvis-file-links':{'mappings':[],'excludePrefixes':[]}})
 def test_upgrade_import_preserves_old_mappings_and_prior_overrides(self):
  package=self.root/'Helpers'/'Notes';package.mkdir(parents=True)
  inherited={'mappings':[{'sourcePrefix':'R:/Notes/','vault':'Notes'}]}
  (package/'wingman.json').write_text(json.dumps({'id':'obsidian-links','config':inherited}))
  _,new,_=setup.plan(self.settings,self.registry,helper_root=package.parent)
  self.assertEqual(new['helperConfig']['obsidian-links'],inherited)
  personal={'mappings':[{'sourcePrefix':'C:/Personal/','vault':'Personal'}],'uriAction':'open'}
  self.settings.write_text(json.dumps({'helperConfig':{'obsidian-links':personal}}))
  _,new,_=setup.plan(self.settings,self.registry,helper_root=package.parent)
  self.assertEqual(new['helperConfig']['obsidian-links'],personal)
  self.assertEqual(json.loads((package/'wingman.json').read_text())['config'],inherited)
 def test_upgrade_import_rejects_missing_directory_and_duplicates(self):
  with self.assertRaises(ValueError):setup.plan(self.settings,self.registry,helper_root=self.root/'missing')
  for name in ['one','two']:
   p=self.root/'Helpers'/name;p.mkdir(parents=True)
   (p/'wingman.json').write_text(json.dumps({'id':'same','config':{}}))
  with self.assertRaises(ValueError):setup.plan(self.settings,self.registry,helper_root=self.root/'Helpers')
 def test_duplicate_vault_names_and_missing_paths_are_reported(self):
  paths=[self.vault(parent=self.root/'one'),self.vault(parent=self.root/'two'),self.root/'Missing']
  self.registry.write_text(json.dumps({'vaults':{str(i):{'path':str(p)} for i,p in enumerate(paths)}}))
  _,new,notices=setup.plan(self.settings,self.registry)
  self.assertEqual(new['helperConfig']['obsidian-links']['mappings'],[])
  self.assertTrue(any('Ambiguous' in n for n in notices));self.assertTrue(any('unavailable' in n for n in notices))
 def test_malformed_and_concurrent_settings_are_preserved(self):
  self.settings.write_text('{broken')
  with self.assertRaises(ValueError):setup.plan(self.settings,self.registry)
  self.assertEqual(self.settings.read_text(),'{broken')
  self.settings.write_text('{}');old,new,_=setup.plan(self.settings,self.registry)
  self.settings.write_text('{"changed": true}')
  with self.assertRaises(ValueError):setup.save(self.settings,old,new)
  self.assertEqual(json.loads(self.settings.read_text()),{'changed':True})
