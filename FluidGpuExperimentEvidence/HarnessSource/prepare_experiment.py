"""Prepare the supported F-only validation project; historical fixtures stay archived."""
from pathlib import Path
import runpy

current = Path(__file__).resolve().parents[1] / 'Cleanup20261029/prepare_validation.py'
runpy.run_path(str(current), run_name='__main__')
